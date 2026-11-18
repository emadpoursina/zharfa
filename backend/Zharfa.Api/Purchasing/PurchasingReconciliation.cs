namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;

/// <summary>
/// The developer-run purchasing reconciliation (FR-017, FR-018, research R11): compares the system's
/// totals (<c>mabna.fn_purchase_totals</c> over the synced copy) with the same totals recomputed from a
/// fresh guarded read of the source for one Jalali month and filter. Writes one <c>reconciliation_run</c>
/// row per metric and, on any mismatch, an <c>audit_log</c> entry <c>reconciliation.mismatch</c>. No UI.
/// </summary>
public sealed class PurchasingReconciliation(
    NpgsqlDataSource dataSource,
    IMabnaReader reader,
    ISyncPreflight preflight,
    IOptions<SyncOptions> syncOptions,
    IOptions<PurchasingOptions> purchasingOptions,
    DataModeOptions dataMode,
    TimeProvider clock)
{
    public const string AuditActor = "cli:reconcile";
    public const string MismatchAction = "reconciliation.mismatch";

    private const string NoSuccessfulSyncText =
        "هیچ همگام‌سازی موفقی ثبت نشده است؛ مقایسه انجام نشد. No successful sync; reconciliation not run.";

    private static readonly JsonSerializerOptions DetailJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<ReconciliationReport> RunAsync(ReconcileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var basis = command.Basis ?? purchasingOptions.Value.DiscountBasis;
        ReconciliationReport report;
        try
        {
            report = await ComputeAsync(command, basis, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report = Failed(command, basis, SafeMessage(ex));
        }

        try
        {
            await RecordAsync(report, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The log could not be written. A run whose result is not logged is not a pass.
            return Failed(command, basis, "ثبت نتیجه در پایگاه داده انجام نشد. The reconciliation log could not be written.");
        }

        return report;
    }

    private async Task<ReconciliationReport> ComputeAsync(ReconcileCommand command, string basis, CancellationToken cancellationToken)
    {
        var check = await preflight.CheckAsync(probeLogin: true, cancellationToken);
        if (check.Refused)
        {
            return Failed(command, basis, check.RefusalMessage!);
        }

        var map = check.Map!;
        var basisError = PurchasingDiscountBasis.Validate(basis, dataMode.IsProduction, map.DiscountBasisValue);
        if (basisError is not null)
        {
            return Failed(command, basis, basisError);
        }

        if (!await HasSucceededSyncAsync(cancellationToken))
        {
            return Failed(command, basis, NoSuccessfulSyncText);
        }

        var system = await ReadSystemTotalsAsync(command, basis, cancellationToken);
        var source = await ReadSourceTotalsAsync(map, command, basis, cancellationToken);

        var availability = MetricAvailability.Evaluate(map, dataMode.IsProduction, hasSucceededSync: true, basis);
        var results = new List<MetricComparison>();
        foreach (var metric in ReconcileMetrics.All)
        {
            var reason = availability.TryGetValue(ReconcileMetrics.FigureOf(metric), out var figureReason) ? figureReason : null;
            results.Add(ReconciliationComparator.Compare(metric, system.Get(metric), source.Get(metric), reason));
        }

        return new ReconciliationReport(
            command.MonthText,
            command.FilterKind,
            command.FilterValue,
            basis,
            dataMode.Mode,
            results);
    }

    private ReconciliationReport Failed(ReconcileCommand command, string basis, string reason) =>
        ReconciliationReport.Failed(command.MonthText, command.FilterKind, command.FilterValue, basis, dataMode.Mode, reason);

    private async Task<bool> HasSucceededSyncAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM sync_job WHERE status = 'succeeded')");
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    /// <summary>The system side: the same function the Overview uses, over the synced copy.</summary>
    private async Task<Totals> ReadSystemTotalsAsync(ReconcileCommand command, string basis, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT gross, returns, net, vat FROM mabna.fn_purchase_totals(" +
            "@year::smallint, @month::smallint, @vendor::text, @product::text, @basis::text)");
        cmd.Parameters.AddWithValue("year", command.Year);
        cmd.Parameters.AddWithValue("month", command.Month);
        cmd.Parameters.AddWithValue("vendor", (object?)(command.FilterKind == "vendor" ? command.FilterValue : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("product", (object?)(command.FilterKind == "product" ? command.FilterValue : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("basis", basis);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("fn_purchase_totals returned no row.");
        }

        return new Totals(
            Gross: reader.GetDecimal(0),
            Returns: reader.GetDecimal(1),
            Net: reader.GetDecimal(2),
            Vat: reader.IsDBNull(3) ? null : reader.GetDecimal(3));
    }

    /// <summary>
    /// The source side: fresh guarded reads of the same objects, with the same keyset paging and map
    /// filters as the sync, mapped by the sync's row mapper, then summed by <see cref="SourceTotalsCalculator"/>.
    /// </summary>
    private async Task<Totals> ReadSourceTotalsAsync(SourceMap map, ReconcileCommand command, string basis, CancellationToken cancellationToken)
    {
        var mapping = new PurchaseMapping(map, dataMode.IsProduction);
        var mapper = new PurchaseRowMapper(mapping);
        var pager = new KeysetPager(reader, syncOptions.Value.PageSize);
        var maxRejected = syncOptions.Value.MaxRejectedRows;

        var invoices = await ReadHeadersAsync(pager, mapping.Invoice, mapper.MapInvoice, maxRejected, cancellationToken);
        var returns = await ReadHeadersAsync(pager, mapping.Return, mapper.MapReturn, maxRejected, cancellationToken);
        var lines = await ReadLinesAsync(pager, mapping.Line, mapper, maxRejected, cancellationToken);

        var documents = invoices.Concat(returns).ToList();
        var totals = SourceTotalsCalculator.Compute(
            documents,
            lines,
            command.Year,
            command.Month,
            command.FilterKind == "vendor" ? command.FilterValue : null,
            command.FilterKind == "product" ? command.FilterValue : null,
            basis,
            vatMapped: mapping.Line.Vat is not null);

        return new Totals(totals.Gross, totals.Returns, totals.Net, totals.Vat);
    }

    private static async Task<List<DocumentRecord>> ReadHeadersAsync(
        KeysetPager pager,
        DocumentSource source,
        Func<SourceRow, MappedRow<DocumentRecord>> map,
        int maxRejected,
        CancellationToken cancellationToken)
    {
        var records = new List<DocumentRecord>();
        var rejected = 0;
        await foreach (var page in pager.ReadPagesAsync(
                           new KeysetRequest(source.Object, source.Columns, source.Key, source.Filters),
                           cancellationToken))
        {
            foreach (var row in page)
            {
                var mapped = map(row);
                if (mapped.RejectReason is not null)
                {
                    rejected++;
                    continue;
                }

                records.Add(mapped.Record!);
            }
        }

        if (rejected > maxRejected)
        {
            throw new SyncRowsRejectedException(
                $"{source.Object}: {rejected} rows rejected; limit is {maxRejected}. Sync__MaxRejectedRows.");
        }

        return records;
    }

    private static async Task<List<LineRecord>> ReadLinesAsync(
        KeysetPager pager,
        LineSource source,
        PurchaseRowMapper mapper,
        int maxRejected,
        CancellationToken cancellationToken)
    {
        var records = new List<LineRecord>();
        var rejected = 0;
        await foreach (var page in pager.ReadPagesAsync(
                           new KeysetRequest(source.Object, source.Columns, source.Key, Array.Empty<MabnaFilter>()),
                           cancellationToken))
        {
            foreach (var row in page)
            {
                var mapped = mapper.MapLine(row);
                if (mapped.RejectReason is not null)
                {
                    rejected++;
                    continue;
                }

                records.Add(mapped.Record!);
            }
        }

        if (rejected > maxRejected)
        {
            throw new SyncRowsRejectedException(
                $"{source.Object}: {rejected} rows rejected; limit is {maxRejected}. Sync__MaxRejectedRows.");
        }

        return records;
    }

    /// <summary>Writes one reconciliation_run row per metric and one audit entry per mismatch, in one transaction.</summary>
    private async Task RecordAsync(ReconciliationReport report, CancellationToken cancellationToken)
    {
        var runAt = clock.GetUtcNow();
        var results = report.Results.Count > 0
            ? report.Results
            : ReconcileMetrics.All.Select(metric => new MetricComparison(metric, null, null, null, ReconcileOutcome.Error)).ToList();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var result in results)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO reconciliation_run " +
                "(run_at, jalali_month, filter_kind, filter_value, metric, system_total, source_total, difference, outcome, discount_basis, data_mode) " +
                "VALUES (@run_at, @month, @kind, @value, @metric, @system, @source, @difference, @outcome, @basis, @mode)",
                connection, tx);
            insert.Parameters.AddWithValue("run_at", runAt);
            insert.Parameters.AddWithValue("month", report.Month ?? string.Empty);
            insert.Parameters.AddWithValue("kind", report.FilterKind ?? "none");
            insert.Parameters.AddWithValue("value", (object?)report.FilterValue ?? DBNull.Value);
            insert.Parameters.AddWithValue("metric", result.Metric);
            insert.Parameters.AddWithValue("system", (object?)result.System ?? DBNull.Value);
            insert.Parameters.AddWithValue("source", (object?)result.Source ?? DBNull.Value);
            insert.Parameters.AddWithValue("difference", (object?)result.Difference ?? DBNull.Value);
            insert.Parameters.AddWithValue("outcome", result.Outcome);
            insert.Parameters.AddWithValue("basis", report.Basis ?? PurchasingDiscountBasis.Line);
            insert.Parameters.AddWithValue("mode", dataMode.Mode);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var mismatch in results.Where(r => r.Outcome == ReconcileOutcome.Mismatch))
        {
            await using var audit = new NpgsqlCommand(
                "INSERT INTO audit_log (at, actor, action, detail) VALUES (@at, @actor, @action, @detail)",
                connection, tx);
            audit.Parameters.AddWithValue("at", runAt.UtcDateTime);
            audit.Parameters.AddWithValue("actor", AuditActor);
            audit.Parameters.AddWithValue("action", MismatchAction);
            audit.Parameters.AddWithValue("detail", MismatchAuditDetail(report, mismatch, dataMode));
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Period, filter and metric only, in every data mode: never the amounts (system, source, difference), which stay
    /// in the reconciliation result and the <c>reconciliation_run</c> rows (FR-015, data-model §2.2).
    /// </summary>
    public static string MismatchAuditDetail(ReconciliationReport report, MetricComparison mismatch, DataModeOptions dataMode)
    {
        var detail = new Dictionary<string, object?>
        {
            ["month"] = report.Month,
            ["filter"] = report.FilterKind,
            ["filterValue"] = report.FilterValue,
            ["metric"] = mismatch.Metric,
            ["basis"] = report.Basis,
            ["dataMode"] = dataMode.Mode,
        };

        return RedactionHelper.Scrub(JsonSerializer.Serialize(detail, DetailJson));
    }

    /// <summary>Admin-safe text for a failed run: no hosts, logins or real object names.</summary>
    private static string SafeMessage(Exception ex) => ex switch
    {
        MabnaSourceUnavailableException => RedactionHelper.Scrub(ex.Message),
        MabnaAccessDeniedException => "دسترسی به منبع مجاز نیست. Source access refused.",
        SyncRowsRejectedException => RedactionHelper.Scrub(ex.Message),
        NpgsqlException or Npgsql.PostgresException =>
            "خطای پایگاه داده در مقایسه. Database error during reconciliation.",
        _ => $"خطای داخلی در مقایسه ({ex.GetType().Name}). Internal reconciliation error.",
    };

    private sealed record Totals(decimal? Gross, decimal? Returns, decimal? Net, decimal? Vat)
    {
        public decimal? Get(string metric) => metric switch
        {
            ReconcileMetrics.Net => Net,
            ReconcileMetrics.Gross => Gross,
            ReconcileMetrics.Returns => Returns,
            ReconcileMetrics.Vat => Vat,
            _ => null,
        };
    }
}

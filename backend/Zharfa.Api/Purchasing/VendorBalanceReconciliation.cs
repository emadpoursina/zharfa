namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using Zharfa.Api.Analytics;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Platform;
using Zharfa.Api.VendorFinancials;

/// <summary>Result of one <c>reconcile vendor-balance</c> run: the printed JSON line, the exit code and the sign-off decision.</summary>
public sealed record VendorBalanceReport(
    string? Month,
    string? FilterKind,
    string? FilterValue,
    string? DataMode,
    IReadOnlyList<MetricComparison> Results,
    string? ErrorReason = null)
{
    /// <summary>
    /// error (a metric could not be compared, or the run failed) > mismatch > unavailable (every metric
    /// unavailable — exit 0, but NOT a sign-off pass) > pass.
    /// </summary>
    public string Outcome =>
        ErrorReason is not null || Results.Any(r => r.Outcome == ReconcileOutcome.Error)
            ? ReconcileOutcome.Error
            : Results.Any(r => r.Outcome == ReconcileOutcome.Mismatch)
                ? ReconcileOutcome.Mismatch
                : Results.Count > 0 && Results.All(r => r.Outcome == ReconcileOutcome.Unavailable)
                    ? ReconcileOutcome.Unavailable
                    : ReconcileOutcome.Pass;

    /// <summary>Sign-off needs a pass: a mismatch, an error or an unavailable formula never signs off (T040).</summary>
    public bool SignOffBlocked => Outcome != ReconcileOutcome.Pass;

    /// <summary>0 pass, 0 unavailable (listed but never fails the run), 1 mismatch, 2 error (contracts/cli-and-config.md).</summary>
    public int ExitCode => Outcome switch
    {
        ReconcileOutcome.Pass or ReconcileOutcome.Unavailable => 0,
        ReconcileOutcome.Mismatch => 1,
        _ => 2,
    };

    public static VendorBalanceReport Failed(string? month, string? filterKind, string? filterValue, string? dataMode, string reason) =>
        new(month, filterKind, filterValue, dataMode, Array.Empty<MetricComparison>(), reason);

    /// <summary>The printed JSON line. Production omits system/source/difference unless <paramref name="showAmounts"/>.</summary>
    public string ToJsonLine(bool showAmounts)
    {
        var isProduction = string.Equals(DataMode, "production", StringComparison.Ordinal);
        var includeAmounts = !isProduction || showAmounts;

        var results = Results.Select(r => new ResultLine(
            r.Metric,
            includeAmounts && r.System is not null ? ReconciliationComparator.Format(r.System.Value) : null,
            includeAmounts && r.Source is not null ? ReconciliationComparator.Format(r.Source.Value) : null,
            includeAmounts && r.Difference is not null ? ReconciliationComparator.Format(r.Difference.Value) : null,
            r.Outcome,
            r.UnavailableReason)).ToList();

        return JsonSerializer.Serialize(
            new ReportLine(
                Month,
                FilterKind is null ? null : new FilterLine(FilterKind, FilterValue),
                DataMode,
                results,
                Outcome,
                SignOffBlocked,
                ErrorReason),
            JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record FilterLine(string Kind, string? Value);

    private sealed record ResultLine(
        string Metric,
        string? System,
        string? Source,
        string? Difference,
        string Outcome,
        string? Reason);

    private sealed record ReportLine(
        string? Month,
        FilterLine? Filter,
        string? DataMode,
        IReadOnlyList<ResultLine> Results,
        string Outcome,
        bool SignOffBlocked,
        string? Reason);
}

/// <summary>
/// The developer-run vendor-balance reconciliation (FR-021/FR-022, research R11, tasks.md T041): compares
/// <c>mabna.fn_vendor_balance</c> over the synced copy with the same candidate-mix formula recomputed from a
/// guarded read of the source (<see cref="VendorFinancialTotals"/>), one Jalali month, optional vendor filter.
/// Writes one <c>reconciliation_run</c> row (metric <c>vendor_balance</c>, discount_basis '-') and, on a
/// mismatch, an <c>audit_log</c> <c>reconciliation.mismatch</c> entry with period/filter/metric only — never
/// amounts. Purchasing reconcile is untouched.
/// </summary>
public sealed class VendorBalanceReconciliation(
    NpgsqlDataSource dataSource,
    IMabnaReader reader,
    ISyncPreflight preflight,
    IOptions<SyncOptions> syncOptions,
    DataModeOptions dataMode,
    TimeProvider clock)
{
    public const string Metric = "vendor_balance";
    public const string AuditActor = "cli:reconcile";
    public const string MismatchAction = "reconciliation.mismatch";

    /// <summary>discount_basis is unused by the balance mix; reconciliation_run stores '-' (data-model §5).</summary>
    public const string DiscountBasisValue = "-";

    private const string NoSuccessfulSyncText =
        "هیچ همگام‌سازی موفقی ثبت نشده است؛ مقایسه انجام نشد. No successful sync; reconciliation not run.";

    private static readonly JsonSerializerOptions DetailJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<VendorBalanceReport> RunAsync(ReconcileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var filterKind = command.FilterKind == "vendor" ? "vendor" : "none";
        var filterValue = filterKind == "vendor" ? command.FilterValue : null;
        VendorBalanceReport report;
        try
        {
            report = await ComputeAsync(command, filterKind, filterValue, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            report = VendorBalanceReport.Failed(command.MonthText, filterKind, filterValue, dataMode.Mode, SafeMessage(ex));
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
            // A run whose result is not logged is not a pass.
            return VendorBalanceReport.Failed(
                command.MonthText, filterKind, filterValue, dataMode.Mode,
                "ثبت نتیجه در پایگاه داده انجام نشد. The reconciliation log could not be written.");
        }

        return report;
    }

    private async Task<VendorBalanceReport> ComputeAsync(
        ReconcileCommand command, string filterKind, string? filterValue, CancellationToken cancellationToken)
    {
        var check = await preflight.CheckAsync(probeLogin: true, cancellationToken);
        if (check.Refused)
        {
            return VendorBalanceReport.Failed(command.MonthText, filterKind, filterValue, dataMode.Mode, check.RefusalMessage!);
        }

        var map = check.Map!;
        if (!await HasSucceededSyncAsync(cancellationToken))
        {
            return VendorBalanceReport.Failed(command.MonthText, filterKind, filterValue, dataMode.Mode, NoSuccessfulSyncText);
        }

        var availability = VendorFinancialAvailability.Evaluate(map, dataMode.IsProduction, hasSucceededSync: true);
        var reason = availability.TryGetValue(VendorFinancialAvailability.FigureVendorBalance, out var value) ? value : null;
        if (reason is not null)
        {
            // Unusable formula: listed as unavailable with its reason; exit 0 and NOT a sign-off pass (T040).
            return new VendorBalanceReport(
                command.MonthText,
                filterKind,
                filterValue,
                dataMode.Mode,
                [new MetricComparison(Metric, null, null, null, ReconcileOutcome.Unavailable, reason)]);
        }

        var asOf = AsOfDate(command.Year, command.Month);
        var vendor = filterKind == "vendor" ? filterValue : null;

        var system = await ReadSystemAsync(asOf, vendor, cancellationToken);
        var source = await ReadSourceAsync(map, asOf, vendor, cancellationToken);
        var comparison = ReconciliationComparator.Compare(Metric, system, source, unavailableReason: null);

        return new VendorBalanceReport(
            command.MonthText, filterKind, filterValue, dataMode.Mode, [comparison]);
    }

    /// <summary>The last Jalali day of the reconciliation month (the as-of of the balance).</summary>
    public static DateOnly AsOfDate(int year, int month) =>
        new JalaliDate(year, month, JalaliDate.DaysInMonth(year, month)).ToDateOnly();

    /// <summary>The system side: the reporting function over the synced copy.</summary>
    private async Task<decimal> ReadSystemAsync(DateOnly asOf, string? vendor, CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand("SELECT mabna.fn_vendor_balance(@asOf::date, @vendor::text)");
        cmd.Parameters.AddWithValue("asOf", asOf);
        cmd.Parameters.AddWithValue("vendor", (object?)vendor ?? DBNull.Value);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is decimal amount ? amount : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The source side: a fresh guarded read of the same two objects (same map, SELECT-only allowlist), mapped
    /// by the sync's row mapper and summed by <see cref="VendorFinancialTotals"/> — the twin of the SQL function.
    /// </summary>
    private async Task<decimal> ReadSourceAsync(SourceMap map, DateOnly asOf, string? vendor, CancellationToken cancellationToken)
    {
        var mapping = new VendorFinancialMapping(map, dataMode.IsProduction);
        if (mapping.PayableDocument is null || mapping.PayableCheque is null)
        {
            // Availability said usable, so this means the map changed under us: treat as unavailable, never guess.
            throw new InvalidOperationException("financial source entities are not usable in this data mode.");
        }

        var mapper = new VendorFinancialRowMapper(mapping);
        var pager = new KeysetPager(reader, syncOptions.Value.PageSize);
        var maxRejected = syncOptions.Value.MaxRejectedRows;

        var documents = await ReadRowsAsync(pager, mapping.PayableDocument, mapper.MapPayableDocument, maxRejected, cancellationToken);
        var cheques = await ReadRowsAsync(pager, mapping.PayableCheque, mapper.MapPayableCheque, maxRejected, cancellationToken);

        return VendorFinancialTotals.VendorBalance(
            documents.Select(d => new PayableDocumentRow(d.VendorSourceKey, d.DocDate, d.RemainingAmount, d.Cancelled)),
            cheques.Select(c => new PayableChequeRow(c.VendorSourceKey, c.IssueDate, c.RemainingAmount, c.ClearedAt, c.Cancelled)),
            asOf,
            vendor);
    }

    private static async Task<List<T>> ReadRowsAsync<T>(
        KeysetPager pager,
        object source,
        Func<SourceRow, MappedRow<T>> map,
        int maxRejected,
        CancellationToken cancellationToken) where T : class
    {
        var (objectName, columns, keyColumn, filters) = source switch
        {
            PayableDocumentSource d => (d.Object, d.Columns, d.Key, d.Filters),
            PayableChequeSource c => (c.Object, c.Columns, c.Key, c.Filters),
            _ => throw new InvalidOperationException("unknown financial source type"),
        };

        var records = new List<T>();
        var rejected = 0;
        await foreach (var page in pager.ReadPagesAsync(
                           new KeysetRequest(objectName, columns, keyColumn, filters), cancellationToken))
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
                $"{objectName}: {rejected} rows rejected; limit is {maxRejected}. Sync__MaxRejectedRows.");
        }

        return records;
    }

    private async Task<bool> HasSucceededSyncAsync(CancellationToken cancellationToken)
    {
        await using var cmd = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM sync_job WHERE status = 'succeeded')");
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is bool exists && exists;
    }

    /// <summary>One reconciliation_run row (metric vendor_balance, discount_basis '-') plus audit on mismatch, in one transaction.</summary>
    private async Task RecordAsync(VendorBalanceReport report, CancellationToken cancellationToken)
    {
        var runAt = clock.GetUtcNow();
        var results = report.Results.Count > 0
            ? report.Results
            : [new MetricComparison(Metric, null, null, null, ReconcileOutcome.Error)];

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
            insert.Parameters.AddWithValue("basis", DiscountBasisValue);
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
    /// Period, filter and metric only, in every data mode: never the amounts (system, source, difference), which
    /// stay in the reconciliation result and the reconciliation_run rows (FR-015).
    /// </summary>
    public static string MismatchAuditDetail(VendorBalanceReport report, MetricComparison mismatch, DataModeOptions dataMode)
    {
        var detail = new Dictionary<string, object?>
        {
            ["month"] = report.Month,
            ["filter"] = report.FilterKind,
            ["filterValue"] = report.FilterValue,
            ["metric"] = mismatch.Metric,
            ["basis"] = DiscountBasisValue,
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
}

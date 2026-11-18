namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna;

/// <summary>
/// Runs one purchasing sync inside a single PostgreSQL transaction (FR-010, SC-003): masters,
/// headers and lines are read through the connector (keyset pages), mapped, and written to the
/// <c>mabna</c> schema together with the per-table watermarks in <c>source_record</c>. Any failure
/// rolls the whole run back, so the previous data and watermarks stay as they were.
///
/// Order: vendor → product → invoice → return → lines. Header tables (invoice, return) are read in
/// full (idWatermark) or through their change marker plus a key-only pass (rowversion, modifiedAt),
/// so a header that is deleted or cancelled at the source reaches the copy on the next run; its lines
/// go with it (ON DELETE CASCADE). Lines are always read in full and replaced as a set: a stored line
/// is kept only when the full read still returns it. Lines whose document is not a synced purchase
/// document are skipped (they belong to other source documents).
///
/// Rejected rows (unparseable date, missing key, negative amount…) are not stored. More than
/// <see cref="SyncRunRequest.MaxRejectedRows"/> in one table fails the run.
/// </summary>
public sealed class SyncRunner(NpgsqlDataSource dataSource, IMabnaReader reader) : ISyncRunWorker
{
    public async Task<SyncRunResult> RunAsync(SyncRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var completed = new List<SyncTableCounts>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var mapping = new PurchaseMapping(request.Map, request.IsProduction);
            var mapper = new PurchaseRowMapper(mapping);
            var pager = new KeysetPager(reader, request.PageSize);

            await using (var stage = Cmd(connection, tx, StageTablesSql))
            {
                await stage.ExecuteNonQueryAsync(cancellationToken);
            }

            var states = await LoadStatesAsync(connection, tx, cancellationToken);
            var run = new RunState(connection, tx, request, mapping, mapper, pager, states);

            completed.Add(await SyncMastersAsync(run, LogicalTables.Vendor, mapping.Vendor, MasterKind.Vendor, cancellationToken));
            AfterTable(request, completed);

            completed.Add(await SyncMastersAsync(run, LogicalTables.Product, mapping.Product, MasterKind.Product, cancellationToken));
            AfterTable(request, completed);

            // Phase 2, optional: the drug group master is synced only when its object is mapped and usable.
            if (mapping.ProductCategory is { } categorySource)
            {
                completed.Add(await SyncMastersAsync(run, LogicalTables.ProductCategory, categorySource, MasterKind.ProductCategory, cancellationToken));
                AfterTable(request, completed);
            }

            completed.Add(await SyncDocumentsAsync(run, LogicalTables.Invoice, mapping.Invoice, "invoice", cancellationToken));
            AfterTable(request, completed);

            completed.Add(await SyncDocumentsAsync(run, LogicalTables.Return, mapping.Return, "return", cancellationToken));
            AfterTable(request, completed);

            completed.Add(await SyncLinesAsync(run, cancellationToken));
            AfterTable(request, completed);

            await RelinkAsync(connection, tx, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new SyncRunResult(completed);
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // The transaction is already unusable; the failure below is what the Admin needs.
            }

            throw new SyncRunException(SafeErrorMessage(ex), completed, ex);
        }
    }

    /// <summary>
    /// Admin-visible text for a failed run. Connector messages are already free of hosts and logins;
    /// object names and database details are never copied from other exceptions.
    /// </summary>
    public static string SafeErrorMessage(Exception ex) => ex switch
    {
        SyncRunException => RedactionHelper.Scrub(ex.Message),
        SyncRowsRejectedException => RedactionHelper.Scrub(ex.Message),
        SyncFaultInjectedException => RedactionHelper.Scrub(ex.Message),
        MabnaSourceUnavailableException => RedactionHelper.Scrub(ex.Message),
        MabnaAccessDeniedException => "دسترسی به منبع مجاز نیست. Source access refused.",
        OperationCanceledException => "همگام‌سازی لغو شد. Sync was cancelled.",
        Npgsql.NpgsqlException or Npgsql.PostgresException =>
            $"خطای پایگاه داده در همگام‌سازی ({ex.GetType().Name}). Database error during sync.",
        _ => $"خطای داخلی در همگام‌سازی ({ex.GetType().Name}). Internal sync error.",
    };

    private static void AfterTable(SyncRunRequest request, List<SyncTableCounts> completed)
    {
        if (request.FailAfterTables is { } after && completed.Count >= after)
        {
            throw new SyncFaultInjectedException(
                "خطای تزریق‌شده برای آزمون توسعه. Injected development fault after the configured table count.");
        }
    }

    // ---------------------------------------------------------------- masters

    private enum MasterKind
    {
        Vendor,
        Product,
        ProductCategory,
    }

    private async Task<SyncTableCounts> SyncMastersAsync(
        RunState run,
        string table,
        MasterSource source,
        MasterKind kind,
        CancellationToken cancellationToken)
    {
        // Masters are read in full on every run with a key diff, whatever change marker the map gives
        // them (research R5): the marker is ignored, so no watermark is kept and no key pass is needed.
        var state = run.StateOf(table);
        var plan = IncrementalPlanner.PlanTable(source.Key, source.Change, state, run.IsFullRun, isDocument: false);
        var counters = new TableCounters(table);

        await foreach (var page in run.Pager.ReadPagesAsync(
                           new KeysetRequest(source.Object, source.Columns, plan.PageColumn, WithChangePredicate(source.Filters, plan)),
                           cancellationToken))
        {
            var batch = new Dictionary<string, MasterRecord>(StringComparer.Ordinal);
            foreach (var row in page)
            {
                counters.Read++;
                var mapped = kind switch
                {
                    MasterKind.Vendor => run.Mapper.MapVendor(row),
                    MasterKind.Product => run.Mapper.MapProduct(row),
                    _ => run.Mapper.MapProductCategory(row),
                };
                if (mapped.RejectReason is not null)
                {
                    counters.Reject(mapped.RejectReason, PurchaseRowMapper.KeyText(row.Get(source.Key)));
                    continue;
                }

                batch[mapped.Record!.SourceKey] = mapped.Record;
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var (inserted, updated) = await UpsertMastersAsync(run, kind, batch.Values.ToList(), cancellationToken);
            counters.Inserted += inserted;
            counters.Updated += updated;
            var stageSql = kind switch
            {
                MasterKind.Vendor => StageVendorSql,
                MasterKind.Product => StageProductSql,
                _ => StageCategorySql,
            };
            await StageKeysAsync(run, stageSql, batch.Keys.ToArray(), cancellationToken);
        }

        counters.EnsureWithinRejectLimit(run.Request.MaxRejectedRows);

        if (plan.DeleteScope == DeleteScope.AllUnseen)
        {
            counters.Deleted = await DeleteUnseenMastersAsync(run, kind, cancellationToken);
        }

        await SaveStateAsync(run, table, new ChangeMarker(ChangeStrategy.Full, null), null, fullRead: true, state, cancellationToken);
        return counters.ToCounts();
    }

    private static async Task<(int Inserted, int Updated)> UpsertMastersAsync(
        RunState run,
        MasterKind kind,
        IReadOnlyList<MasterRecord> records,
        CancellationToken cancellationToken)
    {
        var keys = records.Select(r => r.SourceKey).ToArray();
        var names = records.Select(r => r.NameFa).ToArray();

        NpgsqlCommand cmd;
        switch (kind)
        {
            case MasterKind.Vendor:
                cmd = Cmd(run.Connection, run.Tx, UpsertVendorSql);
                cmd.Parameters.AddWithValue("keys", keys);
                cmd.Parameters.AddWithValue("names", names);
                cmd.Parameters.AddWithValue("actives", records.Select(r => r.IsActiveSource is null ? null : (r.IsActiveSource.Value ? "true" : "false")).ToArray());
                cmd.Parameters.AddWithValue("codes", records.Select(r => r.Code).ToArray());
                break;
            case MasterKind.Product:
                cmd = Cmd(run.Connection, run.Tx, UpsertProductSql);
                cmd.Parameters.AddWithValue("keys", keys);
                cmd.Parameters.AddWithValue("names", names);
                cmd.Parameters.AddWithValue("groups", records.Select(r => r.CategoryKey).ToArray());
                break;
            default:
                cmd = Cmd(run.Connection, run.Tx, UpsertCategorySql);
                cmd.Parameters.AddWithValue("keys", keys);
                cmd.Parameters.AddWithValue("names", names);
                break;
        }

        cmd.Parameters.AddWithValue("synced_at", run.Request.StartedAt);
        await using (cmd)
        {
            return await ReadCountsAsync(cmd, cancellationToken);
        }
    }

    private static async Task<int> DeleteUnseenMastersAsync(RunState run, MasterKind kind, CancellationToken cancellationToken)
    {
        if (kind == MasterKind.Vendor)
        {
            await using var detach = Cmd(run.Connection, run.Tx, DetachVendorSql);
            await detach.ExecuteNonQueryAsync(cancellationToken);
            await using var delete = Cmd(run.Connection, run.Tx, DeleteVendorSql);
            return Convert.ToInt32(await delete.ExecuteScalarAsync(cancellationToken));
        }

        if (kind == MasterKind.ProductCategory)
        {
            // Product rows keep their drug_group_key text; a deleted category falls into the unknown bucket.
            await using var deleteCategory = Cmd(run.Connection, run.Tx, DeleteCategorySql);
            return Convert.ToInt32(await deleteCategory.ExecuteScalarAsync(cancellationToken));
        }

        await using var detachProduct = Cmd(run.Connection, run.Tx, DetachProductSql);
        await detachProduct.ExecuteNonQueryAsync(cancellationToken);
        await using var deleteProduct = Cmd(run.Connection, run.Tx, DeleteProductSql);
        return Convert.ToInt32(await deleteProduct.ExecuteScalarAsync(cancellationToken));
    }

    // ---------------------------------------------------------------- headers

    private async Task<SyncTableCounts> SyncDocumentsAsync(
        RunState run,
        string table,
        DocumentSource source,
        string docType,
        CancellationToken cancellationToken)
    {
        var state = run.StateOf(table);

        // Header tables are read in full (idWatermark) or by change marker plus a key-only pass
        // (rowversion, modifiedAt), so that deletions and cancellations reach the copy on every run.
        var plan = IncrementalPlanner.PlanTable(source.Key, source.Change, state, run.IsFullRun, isDocument: true);
        var tracker = new ChangeTracker(source.Change, source.Key, state.Watermark);
        var counters = new TableCounters(table);

        // Keys of rows that were read but cannot be stored. A full load would not store them, so an
        // incremental run must drop any older copy of such a row (see the rejected-key delete below).
        var rejectedKeys = new List<string>();

        await foreach (var page in run.Pager.ReadPagesAsync(
                           new KeysetRequest(source.Object, source.Columns, plan.PageColumn, WithChangePredicate(source.Filters, plan)),
                           cancellationToken))
        {
            var batch = new Dictionary<string, DocumentRecord>(StringComparer.Ordinal);
            foreach (var row in page)
            {
                counters.Read++;
                var keyText = PurchaseRowMapper.KeyText(row.Get(source.Key));
                if (!tracker.Observe(row))
                {
                    counters.Reject("change-marker-invalid", keyText);
                    AddRejectedKey(rejectedKeys, keyText);
                    continue;
                }

                var mapped = docType == "invoice" ? run.Mapper.MapInvoice(row) : run.Mapper.MapReturn(row);
                if (mapped.RejectReason is not null)
                {
                    counters.Reject(mapped.RejectReason, keyText);
                    AddRejectedKey(rejectedKeys, keyText);
                    continue;
                }

                if (mapped.Warning is not null)
                {
                    counters.Warnings++;
                }

                batch[mapped.Record!.SourceKey] = mapped.Record;
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var (inserted, updated) = await UpsertDocumentsAsync(run, batch.Values.ToList(), cancellationToken);
            counters.Inserted += inserted;
            counters.Updated += updated;
            await StageDocumentsAsync(run, docType, batch.Keys.ToArray(), cancellationToken);
        }

        if (plan.KeyPass)
        {
            // Stage every source key, not only the changed rows, so the deletes below see the whole source.
            await StageAllDocumentKeysAsync(run, source, docType, cancellationToken);
        }

        counters.EnsureWithinRejectLimit(run.Request.MaxRejectedRows);

        if (plan.KeyPass && rejectedKeys.Count > 0)
        {
            // The key-only pass staged these keys, so the delete below would keep their stale copies.
            var (documents, lines) = await DeleteRejectedDocumentsAsync(run, docType, rejectedKeys, cancellationToken);
            counters.Deleted += documents;
            run.CascadedLineDeletes += lines;
        }

        if (plan.DeleteScope == DeleteScope.AllUnseen)
        {
            var (documents, lines) = await DeleteUnseenDocumentsAsync(run, docType, cancellationToken);
            counters.Deleted += documents;
            run.CascadedLineDeletes += lines;
        }

        await SaveStateAsync(run, table, source.Change, tracker.Watermark, plan.FullRead, state, cancellationToken);
        return counters.ToCounts();
    }

    private static void AddRejectedKey(List<string> rejectedKeys, string? keyText)
    {
        if (keyText is not null)
        {
            rejectedKeys.Add(keyText);
        }
    }

    /// <summary>Key-only read of the whole source object (same filters, no change predicate), staged for deletes.</summary>
    private static async Task StageAllDocumentKeysAsync(
        RunState run,
        DocumentSource source,
        string docType,
        CancellationToken cancellationToken)
    {
        await foreach (var page in run.Pager.ReadPagesAsync(
                           new KeysetRequest(source.Object, [source.Key], source.Key, source.Filters),
                           cancellationToken))
        {
            var keys = page
                .Select(row => PurchaseRowMapper.KeyText(row.Get(source.Key)))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (keys.Length > 0)
            {
                await StageDocumentsAsync(run, docType, keys, cancellationToken);
            }
        }
    }

    private static async Task<(int Inserted, int Updated)> UpsertDocumentsAsync(
        RunState run,
        IReadOnlyList<DocumentRecord> records,
        CancellationToken cancellationToken)
    {
        var cmd = Cmd(run.Connection, run.Tx, UpsertDocumentSql);
        cmd.Parameters.AddWithValue("keys", records.Select(r => r.SourceKey).ToArray());
        cmd.Parameters.AddWithValue("types", records.Select(r => r.DocType).ToArray());
        cmd.Parameters.AddWithValue("numbers", records.Select(r => r.DocNumber).ToArray());
        cmd.Parameters.AddWithValue("vendors", records.Select(r => r.VendorSourceKey).ToArray());
        cmd.Parameters.AddWithValue("dates", records.Select(r => r.DocDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("jys", records.Select(r => r.JalaliYear.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("jms", records.Select(r => r.JalaliMonth.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("jds", records.Select(r => r.JalaliDay.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("cancelled", records.Select(r => r.Cancelled ? "true" : "false").ToArray());
        cmd.Parameters.AddWithValue("discounts", records.Select(r => r.DocDiscount.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("synced_at", run.Request.StartedAt);
        await using (cmd)
        {
            return await ReadCountsAsync(cmd, cancellationToken);
        }
    }

    /// <summary>
    /// Deletes local documents of one type whose key was not staged by this run. Callers run it only
    /// after a complete key set was staged (a full read or the key-only pass). Returns the document
    /// count and the purchase lines removed with them by ON DELETE CASCADE (sync_log, T106).
    /// </summary>
    private static async Task<(int Documents, int Lines)> DeleteUnseenDocumentsAsync(RunState run, string docType, CancellationToken cancellationToken)
    {
        await using var cmd = Cmd(run.Connection, run.Tx, DeleteUnseenDocumentsSql);
        cmd.Parameters.AddWithValue("doc_type", docType);
        return await ReadDeleteCountsAsync(cmd, cancellationToken);
    }

    /// <summary>
    /// Deletes the local copies of changed rows that cannot be stored (see <c>SyncDocumentsAsync</c>).
    /// Returns the document count and the cascaded line count.
    /// </summary>
    private static async Task<(int Documents, int Lines)> DeleteRejectedDocumentsAsync(
        RunState run,
        string docType,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        await using var cmd = Cmd(run.Connection, run.Tx, DeleteRejectedDocumentsSql);
        cmd.Parameters.AddWithValue("doc_type", docType);
        cmd.Parameters.AddWithValue("keys", keys.Distinct(StringComparer.Ordinal).ToArray());
        return await ReadDeleteCountsAsync(cmd, cancellationToken);
    }

    /// <summary>Reads the (documents, cascaded lines) pair of a header delete statement.</summary>
    private static async Task<(int Documents, int Lines)> ReadDeleteCountsAsync(NpgsqlCommand cmd, CancellationToken cancellationToken)
    {
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0);
        }

        return (checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)));
    }

    // ---------------------------------------------------------------- lines

    private async Task<SyncTableCounts> SyncLinesAsync(RunState run, CancellationToken cancellationToken)
    {
        var source = run.Mapping.Line;
        var counters = new TableCounters(LogicalTables.Line);
        var documents = await LoadDocumentIndexAsync(run, cancellationToken);

        await foreach (var page in run.Pager.ReadPagesAsync(
                           new KeysetRequest(source.Object, source.Columns, source.Key, Array.Empty<MabnaFilter>()),
                           cancellationToken))
        {
            var batch = new Dictionary<(long Document, string Key), (string DocumentKey, LineRecord Record)>();
            foreach (var row in page)
            {
                counters.Read++;
                var mapped = run.Mapper.MapLine(row);
                if (mapped.RejectReason is not null)
                {
                    counters.Reject(mapped.RejectReason, PurchaseRowMapper.KeyText(row.Get(source.Key)));
                    continue;
                }

                var record = mapped.Record!;
                if (!documents.TryGetValue(record.DocumentSourceKey, out var ids) || (ids.Invoice is null && ids.Return is null))
                {
                    counters.Note("non-purchase-document");
                    continue;
                }

                if (ids.Invoice is not null && ids.Return is not null)
                {
                    counters.Reject("ambiguous-document", record.SourceKey);
                    continue;
                }

                var documentId = (ids.Invoice ?? ids.Return)!.Value;
                if (mapped.Warning is not null)
                {
                    counters.Warnings++;
                }

                batch[(documentId, record.SourceKey)] = (record.DocumentSourceKey, record);
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var (inserted, updated) = await UpsertLinesAsync(run, batch, cancellationToken);
            counters.Inserted += inserted;
            counters.Updated += updated;
            await StageLinesAsync(run, batch.Keys, cancellationToken);
        }

        counters.EnsureWithinRejectLimit(run.Request.MaxRejectedRows);

        // Lines are always read in full, so every stored line that was not read is gone at the source.
        // Lines removed with their header (ON DELETE CASCADE) are counted here too (T106, FR-009).
        counters.Deleted += run.CascadedLineDeletes;
        await using (var delete = Cmd(run.Connection, run.Tx, DeleteUnseenLinesSql))
        {
            counters.Deleted += Convert.ToInt32(await delete.ExecuteScalarAsync(cancellationToken));
        }

        await SaveStateAsync(run, LogicalTables.Line, new ChangeMarker(ChangeStrategy.Full, null), null, fullRead: true, run.StateOf(LogicalTables.Line), cancellationToken);
        return counters.ToCounts();
    }

    private static async Task<(int Inserted, int Updated)> UpsertLinesAsync(
        RunState run,
        Dictionary<(long Document, string Key), (string DocumentKey, LineRecord Record)> batch,
        CancellationToken cancellationToken)
    {
        var rows = batch.Values.Select(v => v.Record).ToList();
        var cmd = Cmd(run.Connection, run.Tx, UpsertLineSql);
        cmd.Parameters.AddWithValue("docs", batch.Keys.Select(k => k.Document.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("keys", rows.Select(r => r.SourceKey).ToArray());
        cmd.Parameters.AddWithValue("products", rows.Select(r => r.ProductSourceKey).ToArray());
        cmd.Parameters.AddWithValue("quantities", rows.Select(r => r.Quantity?.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("grosses", rows.Select(r => r.GrossAmount.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("discounts", rows.Select(r => r.LineDiscount.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("vats", rows.Select(r => r.VatAmount?.ToString(CultureInfo.InvariantCulture)).ToArray());
        await using (cmd)
        {
            return await ReadCountsAsync(cmd, cancellationToken);
        }
    }

    private async Task<Dictionary<string, DocumentIds>> LoadDocumentIndexAsync(RunState run, CancellationToken cancellationToken)
    {
        var index = new Dictionary<string, DocumentIds>(StringComparer.Ordinal);
        await using var cmd = Cmd(run.Connection, run.Tx, "SELECT doc_type, source_key, document_id FROM mabna.purchase_document");
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var docType = reader.GetString(0);
            var key = reader.GetString(1);
            var id = reader.GetInt64(2);
            index.TryGetValue(key, out var current);
            current ??= new DocumentIds(null, null);
            index[key] = docType == "invoice"
                ? current with { Invoice = id }
                : current with { Return = id };
        }

        return index;
    }

    private sealed record DocumentIds(long? Invoice, long? Return);

    // ---------------------------------------------------------------- shared SQL helpers

    private static async Task StageKeysAsync(RunState run, string sql, string[] keys, CancellationToken cancellationToken)
    {
        await using var cmd = Cmd(run.Connection, run.Tx, sql);
        cmd.Parameters.AddWithValue("keys", keys);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task StageDocumentsAsync(RunState run, string docType, string[] keys, CancellationToken cancellationToken)
    {
        await using var cmd = Cmd(run.Connection, run.Tx, StageDocumentSql);
        cmd.Parameters.AddWithValue("types", keys.Select(_ => docType).ToArray());
        cmd.Parameters.AddWithValue("keys", keys);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task StageLinesAsync(
        RunState run,
        IEnumerable<(long Document, string Key)> lines,
        CancellationToken cancellationToken)
    {
        var list = lines.ToList();
        await using var cmd = Cmd(run.Connection, run.Tx, StageLineSql);
        cmd.Parameters.AddWithValue("docs", list.Select(l => l.Document.ToString(CultureInfo.InvariantCulture)).ToArray());
        cmd.Parameters.AddWithValue("keys", list.Select(l => l.Key).ToArray());
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(int Inserted, int Updated)> ReadCountsAsync(NpgsqlCommand cmd, CancellationToken cancellationToken)
    {
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0);
        }

        return (checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)));
    }

    /// <summary>
    /// Saves the table's watermark and last full load in the same transaction as its rows.
    /// A full read sets <c>last_full_load_at</c>; an incremental read keeps the previous value.
    /// </summary>
    private static async Task SaveStateAsync(
        RunState run,
        string table,
        ChangeMarker marker,
        string? watermark,
        bool fullRead,
        TableSyncState previous,
        CancellationToken cancellationToken)
    {
        var lastFull = fullRead ? run.Request.StartedAt : previous.LastFullLoadAt;
        await using var cmd = Cmd(run.Connection, run.Tx, SaveSourceRecordSql);
        cmd.Parameters.AddWithValue("system", SyncVocabulary.SourceSystem);
        cmd.Parameters.AddWithValue("table", table);
        cmd.Parameters.AddWithValue("strategy", SyncVocabulary.StrategyText(marker.Strategy));
        cmd.Parameters.AddWithValue("watermark", (object?)watermark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("last_full", (object?)lastFull ?? DBNull.Value);
        cmd.Parameters.AddWithValue("recorded_at", run.Request.StartedAt);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, TableSyncState>> LoadStatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CancellationToken cancellationToken)
    {
        var states = new Dictionary<string, TableSyncState>(StringComparer.Ordinal);
        await using var cmd = Cmd(connection, tx,
            "SELECT table_logical, watermark, last_full_load_at FROM source_record WHERE source_system = @system");
        cmd.Parameters.AddWithValue("system", SyncVocabulary.SourceSystem);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states[reader.GetString(0)] = new TableSyncState(
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
        }

        return states;
    }

    /// <summary>Repoints vendor and product references after the masters are final.</summary>
    private static async Task RelinkAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken cancellationToken)
    {
        await using (var vendors = Cmd(connection, tx, RelinkVendorSql))
        {
            await vendors.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var products = Cmd(connection, tx, RelinkProductSql);
        await products.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand Cmd(NpgsqlConnection connection, NpgsqlTransaction tx, string sql) =>
        new(sql, connection, tx);

    private static long? ParseId(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static IReadOnlyList<MabnaFilter> WithChangePredicate(IReadOnlyList<MabnaFilter> filters, TablePlan plan)
    {
        if (plan.ChangePredicate is null)
        {
            return filters;
        }

        var list = filters.ToList();
        list.Add(plan.ChangePredicate);
        return list;
    }

    private sealed class RunState(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        SyncRunRequest request,
        PurchaseMapping mapping,
        PurchaseRowMapper mapper,
        KeysetPager pager,
        Dictionary<string, TableSyncState> states)
    {
        public NpgsqlConnection Connection { get; } = connection;

        public NpgsqlTransaction Tx { get; } = tx;

        public SyncRunRequest Request { get; } = request;

        public PurchaseMapping Mapping { get; } = mapping;

        public PurchaseRowMapper Mapper { get; } = mapper;

        public KeysetPager Pager { get; } = pager;

        /// <summary>Purchase lines removed by ON DELETE CASCADE when their header was deleted in this run.</summary>
        public int CascadedLineDeletes { get; set; }

        public bool IsFullRun => Request.Mode == SyncRunMode.Full;

        public TableSyncState StateOf(string table) =>
            states.TryGetValue(table, out var state) ? state : TableSyncState.Unloaded;
    }

    /// <summary>
    /// Per-table counters. <see cref="SyncTableCounts.Detail"/> holds reason codes, counts and up to
    /// <see cref="SampleLimit"/> rejected source keys with their reason (keys are identifiers, never names or values).
    /// </summary>
    private sealed class TableCounters(string table)
    {
        private const int SampleLimit = 20;

        private readonly SortedDictionary<string, int> _rejects = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> _notes = new(StringComparer.Ordinal);
        private readonly List<string> _samples = new();

        public int Read { get; set; }

        public int Inserted { get; set; }

        public int Updated { get; set; }

        public int Deleted { get; set; }

        public int Rejected { get; private set; }

        public int Warnings { get; set; }

        public void Reject(string reason, string? key = null)
        {
            Rejected++;
            Count(_rejects, reason);
            if (_samples.Count < SampleLimit)
            {
                _samples.Add($"{key ?? "-"}:{reason}");
            }
        }

        public void Note(string code) => Count(_notes, code);

        private static void Count(SortedDictionary<string, int> counts, string code)
        {
            counts.TryGetValue(code, out var current);
            counts[code] = current + 1;
        }

        public void EnsureWithinRejectLimit(int maxRejected)
        {
            if (Rejected > maxRejected)
            {
                throw new SyncRowsRejectedException(
                    $"{table}: {Rejected} rows rejected; limit is {maxRejected}. Sync__MaxRejectedRows.");
            }
        }

        public SyncTableCounts ToCounts()
        {
            var parts = _rejects.Select(kv => $"rejected:{kv.Key}={kv.Value}")
                .Concat(_notes.Select(kv => $"{kv.Key}={kv.Value}"))
                .ToList();
            if (_samples.Count > 0)
            {
                parts.Add($"rejected-sample[{string.Join(",", _samples)}]");
            }

            return new SyncTableCounts(table, Read, Inserted, Updated, Deleted, Rejected, Warnings,
                parts.Count == 0 ? null : string.Join("; ", parts));
        }
    }

    /// <summary>
    /// Watermark tracking for one table. The value is the largest change marker seen in this run,
    /// combined with the stored one; a row whose marker cannot be read fails its own check.
    /// </summary>
    private sealed class ChangeTracker
    {
        private readonly ChangeMarker _marker;
        private readonly string _keyColumn;
        private long? _id;
        private byte[]? _rowversion;
        private DateTime? _modifiedAt;

        public ChangeTracker(ChangeMarker marker, string keyColumn, string? previous)
        {
            _marker = marker;
            _keyColumn = keyColumn;
            switch (marker.Strategy)
            {
                case ChangeStrategy.IdWatermark:
                    _id = ParseId(previous);
                    break;
                case ChangeStrategy.Rowversion when !string.IsNullOrWhiteSpace(previous):
                    _rowversion = IncrementalPlanner.RowversionFromText(previous);
                    break;
                case ChangeStrategy.ModifiedAt when !string.IsNullOrWhiteSpace(previous):
                    _modifiedAt = IncrementalPlanner.ModifiedAtFromText(previous);
                    break;
            }
        }

        public bool Observe(SourceRow row)
        {
            switch (_marker.Strategy)
            {
                case ChangeStrategy.IdWatermark:
                {
                    var raw = row.Get(_marker.Column ?? _keyColumn);
                    if (!PurchaseRowMapper.TryDecimal(raw, out var number) ||
                        number != decimal.Truncate(number) || number < 0 || number > long.MaxValue)
                    {
                        return false;
                    }

                    _id = IncrementalPlanner.NextIdWatermark(_id, [(long)number]);
                    return true;
                }

                case ChangeStrategy.Rowversion when _marker.Column is not null:
                {
                    if (row.Get(_marker.Column) is not byte[] bytes)
                    {
                        return false;
                    }

                    var next = IncrementalPlanner.NextRowversionWatermark(
                        _rowversion is null ? null : IncrementalPlanner.RowversionText(_rowversion),
                        [bytes]);
                    _rowversion = next is null ? null : IncrementalPlanner.RowversionFromText(next);
                    return true;
                }

                case ChangeStrategy.ModifiedAt when _marker.Column is not null:
                {
                    var value = row.Get(_marker.Column);
                    DateTime modified;
                    switch (value)
                    {
                        case DateTime dt:
                            modified = dt;
                            break;
                        case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed):
                            modified = parsed;
                            break;
                        default:
                            return false;
                    }

                    var next = IncrementalPlanner.NextModifiedAtWatermark(
                        _modifiedAt?.ToString("O", CultureInfo.InvariantCulture),
                        [modified]);
                    _modifiedAt = next is null ? null : IncrementalPlanner.ModifiedAtFromText(next);
                    return true;
                }

                default:
                    return true;
            }
        }

        public string? Watermark => _marker.Strategy switch
        {
            ChangeStrategy.IdWatermark => _id?.ToString(CultureInfo.InvariantCulture),
            ChangeStrategy.Rowversion => _rowversion is null ? null : IncrementalPlanner.RowversionText(_rowversion),
            ChangeStrategy.ModifiedAt => _modifiedAt?.ToString("O", CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- SQL

    private const string StageTablesSql = """
        CREATE TEMP TABLE stage_vendor (source_key text PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE stage_product (source_key text PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE stage_category (source_key text PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE stage_document (doc_type text NOT NULL, source_key text NOT NULL, PRIMARY KEY (doc_type, source_key)) ON COMMIT DROP;
        CREATE TEMP TABLE stage_line (document_id bigint NOT NULL, source_key text NOT NULL, PRIMARY KEY (document_id, source_key)) ON COMMIT DROP;
        """;

    private const string StageVendorSql =
        "INSERT INTO stage_vendor (source_key) SELECT k FROM unnest(@keys::text[]) AS x(k) ON CONFLICT DO NOTHING";

    private const string StageProductSql =
        "INSERT INTO stage_product (source_key) SELECT k FROM unnest(@keys::text[]) AS x(k) ON CONFLICT DO NOTHING";

    private const string StageCategorySql =
        "INSERT INTO stage_category (source_key) SELECT k FROM unnest(@keys::text[]) AS x(k) ON CONFLICT DO NOTHING";

    private const string StageDocumentSql =
        "INSERT INTO stage_document (doc_type, source_key) SELECT t, k FROM unnest(@types::text[], @keys::text[]) AS x(t, k) ON CONFLICT DO NOTHING";

    private const string StageLineSql =
        "INSERT INTO stage_line (document_id, source_key) SELECT d::bigint, k FROM unnest(@docs::text[], @keys::text[]) AS x(d, k) ON CONFLICT DO NOTHING";

    private const string UpsertVendorSql = """
        WITH up AS (
            INSERT INTO mabna.vendor (source_key, name_fa, is_active_source, code, synced_at)
            SELECT k, n, NULLIF(a, '')::boolean, NULLIF(c, ''), @synced_at
            FROM unnest(@keys::text[], @names::text[], @actives::text[], @codes::text[]) AS x(k, n, a, c)
            ON CONFLICT (source_key) DO UPDATE
                SET name_fa = EXCLUDED.name_fa,
                    is_active_source = EXCLUDED.is_active_source,
                    code = EXCLUDED.code,
                    synced_at = EXCLUDED.synced_at
                WHERE (mabna.vendor.name_fa, mabna.vendor.is_active_source, mabna.vendor.code)
                      IS DISTINCT FROM (EXCLUDED.name_fa, EXCLUDED.is_active_source, EXCLUDED.code)
            RETURNING (xmax = 0) AS inserted
        )
        SELECT count(*) FILTER (WHERE inserted), count(*) FILTER (WHERE NOT inserted) FROM up
        """;

    private const string UpsertProductSql = """
        WITH up AS (
            INSERT INTO mabna.product (source_key, name_fa, drug_group_key, synced_at)
            SELECT k, n, NULLIF(g, ''), @synced_at
            FROM unnest(@keys::text[], @names::text[], @groups::text[]) AS x(k, n, g)
            ON CONFLICT (source_key) DO UPDATE
                SET name_fa = EXCLUDED.name_fa,
                    drug_group_key = EXCLUDED.drug_group_key,
                    synced_at = EXCLUDED.synced_at
                WHERE (mabna.product.name_fa, mabna.product.drug_group_key)
                      IS DISTINCT FROM (EXCLUDED.name_fa, EXCLUDED.drug_group_key)
            RETURNING (xmax = 0) AS inserted
        )
        SELECT count(*) FILTER (WHERE inserted), count(*) FILTER (WHERE NOT inserted) FROM up
        """;

    private const string UpsertCategorySql = """
        WITH up AS (
            INSERT INTO mabna.product_category (source_key, name_fa, synced_at)
            SELECT k, n, @synced_at
            FROM unnest(@keys::text[], @names::text[]) AS x(k, n)
            ON CONFLICT (source_key) DO UPDATE
                SET name_fa = EXCLUDED.name_fa,
                    synced_at = EXCLUDED.synced_at
                WHERE mabna.product_category.name_fa IS DISTINCT FROM EXCLUDED.name_fa
            RETURNING (xmax = 0) AS inserted
        )
        SELECT count(*) FILTER (WHERE inserted), count(*) FILTER (WHERE NOT inserted) FROM up
        """;

    private const string DeleteCategorySql = """
        WITH gone AS (
            DELETE FROM mabna.product_category c
            WHERE NOT EXISTS (SELECT 1 FROM stage_category s WHERE s.source_key = c.source_key)
            RETURNING 1)
        SELECT count(*) FROM gone
        """;

    private const string DetachVendorSql = """
        UPDATE mabna.purchase_document SET vendor_id = NULL
        WHERE vendor_id IN (SELECT v.vendor_id FROM mabna.vendor v
                            WHERE NOT EXISTS (SELECT 1 FROM stage_vendor s WHERE s.source_key = v.source_key))
        """;

    private const string DeleteVendorSql = """
        WITH gone AS (
            DELETE FROM mabna.vendor v
            WHERE NOT EXISTS (SELECT 1 FROM stage_vendor s WHERE s.source_key = v.source_key)
            RETURNING 1)
        SELECT count(*) FROM gone
        """;

    private const string DetachProductSql = """
        UPDATE mabna.purchase_line SET product_id = NULL
        WHERE product_id IN (SELECT p.product_id FROM mabna.product p
                             WHERE NOT EXISTS (SELECT 1 FROM stage_product s WHERE s.source_key = p.source_key))
        """;

    private const string DeleteProductSql = """
        WITH gone AS (
            DELETE FROM mabna.product p
            WHERE NOT EXISTS (SELECT 1 FROM stage_product s WHERE s.source_key = p.source_key)
            RETURNING 1)
        SELECT count(*) FROM gone
        """;

    private const string UpsertDocumentSql = """
        WITH up AS (
            INSERT INTO mabna.purchase_document
                (source_key, doc_type, doc_number, vendor_source_key, doc_date, jalali_year, jalali_month, jalali_day, cancelled, doc_discount, synced_at)
            SELECT k, t, n, vk, d::date, jy::smallint, jm::smallint, jd::smallint, c::boolean, dd::numeric, @synced_at
            FROM unnest(@keys::text[], @types::text[], @numbers::text[], @vendors::text[], @dates::text[],
                        @jys::text[], @jms::text[], @jds::text[], @cancelled::text[], @discounts::text[])
                AS x(k, t, n, vk, d, jy, jm, jd, c, dd)
            ON CONFLICT (doc_type, source_key) DO UPDATE
                SET doc_number = EXCLUDED.doc_number,
                    vendor_source_key = EXCLUDED.vendor_source_key,
                    doc_date = EXCLUDED.doc_date,
                    jalali_year = EXCLUDED.jalali_year,
                    jalali_month = EXCLUDED.jalali_month,
                    jalali_day = EXCLUDED.jalali_day,
                    cancelled = EXCLUDED.cancelled,
                    doc_discount = EXCLUDED.doc_discount,
                    synced_at = EXCLUDED.synced_at
                WHERE (mabna.purchase_document.doc_number, mabna.purchase_document.vendor_source_key,
                       mabna.purchase_document.doc_date, mabna.purchase_document.jalali_year,
                       mabna.purchase_document.jalali_month, mabna.purchase_document.jalali_day,
                       mabna.purchase_document.cancelled, mabna.purchase_document.doc_discount)
                      IS DISTINCT FROM
                      (EXCLUDED.doc_number, EXCLUDED.vendor_source_key, EXCLUDED.doc_date, EXCLUDED.jalali_year,
                       EXCLUDED.jalali_month, EXCLUDED.jalali_day, EXCLUDED.cancelled, EXCLUDED.doc_discount)
            RETURNING (xmax = 0) AS inserted
        )
        SELECT count(*) FILTER (WHERE inserted), count(*) FILTER (WHERE NOT inserted) FROM up
        """;

    // Each header delete returns (documents deleted, purchase lines removed by ON DELETE CASCADE). The
    // line count is read in the same statement: foreign-key cascades run after the statement, so the
    // lines of the deleted documents are still visible to the sub-select (T106, FR-009).

    /// <summary>Deletes the documents of one type that the run did not stage (lines follow by ON DELETE CASCADE).</summary>
    private const string DeleteUnseenDocumentsSql = """
        WITH gone AS (
            DELETE FROM mabna.purchase_document p
            WHERE p.doc_type = @doc_type
              AND NOT EXISTS (SELECT 1 FROM stage_document s WHERE s.doc_type = p.doc_type AND s.source_key = p.source_key)
            RETURNING p.document_id)
        SELECT (SELECT count(*) FROM gone),
               (SELECT count(*) FROM mabna.purchase_line l WHERE l.document_id IN (SELECT g.document_id FROM gone g))
        """;

    private const string DeleteRejectedDocumentsSql = """
        WITH gone AS (
            DELETE FROM mabna.purchase_document p
            WHERE p.doc_type = @doc_type
              AND p.source_key = ANY(@keys::text[])
            RETURNING p.document_id)
        SELECT (SELECT count(*) FROM gone),
               (SELECT count(*) FROM mabna.purchase_line l WHERE l.document_id IN (SELECT g.document_id FROM gone g))
        """;

    private const string UpsertLineSql = """
        WITH up AS (
            INSERT INTO mabna.purchase_line
                (document_id, source_key, product_source_key, quantity, gross_amount, line_discount, vat_amount)
            SELECT d::bigint, k, pk, q::numeric, g::numeric, ld::numeric, v::numeric
            FROM unnest(@docs::text[], @keys::text[], @products::text[], @quantities::text[], @grosses::text[],
                        @discounts::text[], @vats::text[])
                AS x(d, k, pk, q, g, ld, v)
            ON CONFLICT (document_id, source_key) DO UPDATE
                SET product_source_key = EXCLUDED.product_source_key,
                    quantity = EXCLUDED.quantity,
                    gross_amount = EXCLUDED.gross_amount,
                    line_discount = EXCLUDED.line_discount,
                    vat_amount = EXCLUDED.vat_amount
                WHERE (mabna.purchase_line.product_source_key, mabna.purchase_line.quantity,
                       mabna.purchase_line.gross_amount, mabna.purchase_line.line_discount, mabna.purchase_line.vat_amount)
                      IS DISTINCT FROM
                      (EXCLUDED.product_source_key, EXCLUDED.quantity, EXCLUDED.gross_amount,
                       EXCLUDED.line_discount, EXCLUDED.vat_amount)
            RETURNING (xmax = 0) AS inserted
        )
        SELECT count(*) FILTER (WHERE inserted), count(*) FILTER (WHERE NOT inserted) FROM up
        """;

    private const string DeleteUnseenLinesSql = """
        WITH gone AS (
            DELETE FROM mabna.purchase_line l
            WHERE NOT EXISTS (SELECT 1 FROM stage_line s WHERE s.document_id = l.document_id AND s.source_key = l.source_key)
            RETURNING 1)
        SELECT count(*) FROM gone
        """;

    private const string RelinkVendorSql = """
        UPDATE mabna.purchase_document d
        SET vendor_id = (SELECT v.vendor_id FROM mabna.vendor v WHERE v.source_key = d.vendor_source_key)
        WHERE d.vendor_id IS DISTINCT FROM (SELECT v.vendor_id FROM mabna.vendor v WHERE v.source_key = d.vendor_source_key)
        """;

    private const string RelinkProductSql = """
        UPDATE mabna.purchase_line l
        SET product_id = (SELECT p.product_id FROM mabna.product p WHERE p.source_key = l.product_source_key)
        WHERE l.product_id IS DISTINCT FROM (SELECT p.product_id FROM mabna.product p WHERE p.source_key = l.product_source_key)
        """;

    private const string SaveSourceRecordSql = """
        INSERT INTO source_record (source_system, table_logical, strategy, watermark, last_full_load_at, recorded_at)
        VALUES (@system, @table, @strategy, @watermark::text, @last_full::timestamptz, @recorded_at)
        ON CONFLICT (source_system, table_logical) DO UPDATE
            SET strategy = EXCLUDED.strategy,
                watermark = EXCLUDED.watermark,
                last_full_load_at = COALESCE(EXCLUDED.last_full_load_at, source_record.last_full_load_at),
                recorded_at = EXCLUDED.recorded_at
        """;
}

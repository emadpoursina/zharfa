namespace Zharfa.Api.VendorFinancials;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Zharfa.Api.Purchasing.Reports;

// Parameterized SQL of the vendor financials (specs/008-vendor-financials/tasks.md T014). Fixed templates only;
// every value is bound. Reads the synced PostgreSQL copy exclusively — never Mabna per request (research R1).
// The three figures call the mabna.fn_* functions of migration 0011; net purchases reuse the Phase 1
// fn_purchase_lines range (no fourth function is added).

/// <summary>Raw row of the Financials list before it is shaped into a DTO.</summary>
public sealed record FinancialSummaryRowData(
    string Key,
    string Name,
    string? Code,
    decimal? VendorBalance,
    decimal? OutstandingPayments,
    decimal? PreviousVendorBalance,
    decimal? PreviousOutstandingPayments);

/// <summary>Parsed and validated period/scope of one financial request (pure data, built by the service).</summary>
public sealed record VendorFinancialScope(
    DateOnly From,
    DateOnly To,
    DateOnly AsOf,
    int FromKey,
    int ToKey,
    bool HasPrevious,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    DateOnly PreviousAsOf,
    int PreviousFromKey,
    int PreviousToKey,
    string? Search,
    bool CodeUsable,
    string Basis);

/// <summary>Detail header totals of one vendor (contracts/api.md §2).</summary>
public sealed record FinancialDetailData(
    decimal? VendorBalance,
    decimal? OutstandingPayments,
    decimal? NetPurchases,
    decimal? Payments,
    decimal? PreviousNetPurchases,
    decimal? PreviousPayments);

/// <summary>Vendor header lookup (key may be absent when the vendor is unknown).</summary>
public sealed record FinancialVendorHeader(string? Key, string? Name, string? Code);

/// <summary>One payment-history row (raw date; the service formats the Jalali text).</summary>
public sealed record PaymentHistoryData(DateOnly PaidAt, string? Reference, string InstrumentType, decimal Amount);

/// <summary>One outstanding-instrument row (raw date; the service formats the Jalali text).</summary>
public sealed record OutstandingInstrumentData(DateOnly Date, string? Reference, string InstrumentType, decimal UnpaidAmount);

/// <summary>Sort allowlists (contracts/api.md §1, §3, §4).</summary>
public static class VendorFinancialSorts
{
    public static readonly string[] Summary = ["name", "code", "vendorBalance", "outstandingPayments"];
    public static readonly string[] History = ["paidAt", "amount", "reference"];
    public static readonly string[] Outstanding = ["date", "amount", "reference"];
}

/// <summary>Persian labels of mapped instrument-type codes (research R7). Unknown codes pass through as stored.</summary>
public static class VendorFinancialLabels
{
    public static string InstrumentTypeLabel(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        null or "" => "نامشخص",
        "cheque" or "chq" or "check" => "چک",
        "cash" => "نقد",
        "transfer" or "bank" or "banktransfer" => "حواله بانکی",
        "pos" => "کارتخوان",
        "note" or "bill" => "اسکناس",
        _ => code,
    };
}

/// <summary>Queries of the vendor financials over the synced copy. One command, one 15 s budget.</summary>
public sealed class VendorFinancialSql(NpgsqlDataSource dataSource)
{
    public const int CommandTimeoutSeconds = 15;

    /// <summary>Report timeout mapped to the shared 503 message by the caller.</summary>
    public static string TimeoutMessage => ReportTimeoutException.PersianMessage;

    // ----- summary (contracts/api.md §1) ----------------------------------------------------------------------

    public Task<long> CountSummaryAsync(VendorFinancialScope scope, CancellationToken ct) =>
        CountSummaryAsync(scope, balance: false, outstanding: false, ct);

    /// <summary>
    /// Total matching vendors for the current search. Balance and outstanding flags are gated false
    /// in SummaryTotalsSql so directory count queries do not call mabna.fn_vendor_balance or fn_outstanding_payments (FR-011).
    /// </summary>
    public async Task<long> CountSummaryAsync(
        VendorFinancialScope scope, bool balance, bool outstanding, CancellationToken ct)
    {
        await using var cmd = Command(SummaryTotalsSql(scope, balance: false, outstanding: false));
        BindSummaryList(cmd, scope);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is long count ? count : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    /// <param name="balance">False when the balance figure is unavailable: the query then selects NULL and never
    /// calls <c>mabna.fn_vendor_balance</c> (hard rule: production does not touch it while the formula is a candidate).</param>
    /// <param name="outstanding">False when outstanding payments are unavailable: NULL, no function call.</param>
    public async Task<IReadOnlyList<FinancialSummaryRowData>> ReadSummaryAsync(
        VendorFinancialScope scope, bool balance, bool outstanding, string sort, bool descending, int limit, int offset, CancellationToken ct)
    {
        await using var cmd = Command(SummaryQuerySql(scope, balance, outstanding, SummaryOrder(sort, descending)));
        BindSummaryList(cmd, scope);
        cmd.Parameters.Add(Int("limit", limit));
        cmd.Parameters.Add(Int("offset", offset));

        var rows = new List<FinancialSummaryRowData>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new FinancialSummaryRowData(
                Key: Text(reader, "key") ?? string.Empty,
                Name: Text(reader, "name") ?? string.Empty,
                Code: scope.CodeUsable ? Text(reader, "code") : null,
                VendorBalance: DecOrNothing(reader, "vendor_balance"),
                OutstandingPayments: DecOrNothing(reader, "outstanding_payments"),
                PreviousVendorBalance: DecOrNothing(reader, "prev_balance"),
                PreviousOutstandingPayments: DecOrNothing(reader, "prev_outstanding")));
        }

        return rows;
    }

    /// <summary>Binds exactly the parameters the summary templates reference.</summary>
    private static void BindSummaryList(NpgsqlCommand cmd, VendorFinancialScope scope)
    {
        cmd.Parameters.Add(Date("asOf", scope.AsOf));
        cmd.Parameters.Add(Date("pAsOf", scope.PreviousAsOf));
        cmd.Parameters.Add(Text("q", scope.Search));
    }

    /// <summary>Binds every parameter the detail template references.</summary>
    private static void BindDetail(NpgsqlCommand cmd, VendorFinancialScope scope)
    {
        cmd.Parameters.Add(Date("from", scope.From));
        cmd.Parameters.Add(Date("to", scope.To));
        cmd.Parameters.Add(Date("asOf", scope.AsOf));
        cmd.Parameters.Add(Date("pFrom", scope.PreviousFrom));
        cmd.Parameters.Add(Date("pTo", scope.PreviousTo));
        cmd.Parameters.Add(Int("fromKey", scope.FromKey));
        cmd.Parameters.Add(Int("toKey", scope.ToKey));
        cmd.Parameters.Add(Int("pFromKey", scope.PreviousFromKey));
        cmd.Parameters.Add(Int("pToKey", scope.PreviousToKey));
        cmd.Parameters.Add(Text("basis", scope.Basis));
    }

    // The directory: every vendor of the purchasing master with its as-of balance, as-of outstanding payments and
    // the previous-period pair (ignored when compare is off). A vendor with no payables appears with 0.0000.
    // Purchases are NOT part of the list (they live on the detail's purchases-vs-payments block).
    private const string SummaryDirectoryCte = @"
WITH directory AS (
    SELECT v.source_key AS key,
           v.name_fa AS name,
           {CODE} AS code,
           {SEARCH} AS search_text,
           {BALANCE} AS vendor_balance,
           {OUTSTANDING} AS outstanding_payments,
           {PREV_BALANCE} AS prev_balance,
           {PREV_OUTSTANDING} AS prev_outstanding
    FROM mabna.vendor v
)";

    private const string SummarySearch =
        @"(@q::text IS NULL OR search_text LIKE '%' || mabna.fn_search_key(@q::text) || '%' ESCAPE '\')";

    private static string SummaryCte(VendorFinancialScope scope, bool balance, bool outstanding) =>
        SummaryDirectoryCte
            .Replace("{CODE}", scope.CodeUsable ? "v.code" : "NULL::text", StringComparison.Ordinal)
            .Replace("{SEARCH}", scope.CodeUsable
                ? "mabna.fn_search_key(v.name_fa || '|' || coalesce(v.code, ''))"
                : "mabna.fn_search_key(v.name_fa)", StringComparison.Ordinal)
            .Replace("{BALANCE}", balance ? "mabna.fn_vendor_balance(@asOf::date, v.source_key)" : "NULL::numeric", StringComparison.Ordinal)
            .Replace("{OUTSTANDING}", outstanding ? "mabna.fn_outstanding_payments(@asOf::date, v.source_key)" : "NULL::numeric", StringComparison.Ordinal)
            .Replace("{PREV_BALANCE}", balance && scope.HasPrevious ? "mabna.fn_vendor_balance(@pAsOf::date, v.source_key)" : "NULL::numeric", StringComparison.Ordinal)
            .Replace("{PREV_OUTSTANDING}", outstanding && scope.HasPrevious ? "mabna.fn_outstanding_payments(@pAsOf::date, v.source_key)" : "NULL::numeric", StringComparison.Ordinal);

    public static string SummaryTotalsSql(VendorFinancialScope scope, bool balance = false, bool outstanding = false) =>
        SummaryCte(scope, balance: false, outstanding: false) + "\nSELECT count(*) FROM directory\nWHERE " + SummarySearch;

    private static string SummaryQuerySql(VendorFinancialScope scope, bool balance, bool outstanding, string orderBy) =>
        SummaryCte(scope, balance, outstanding) +
        "\nSELECT key, name, code, vendor_balance, outstanding_payments, prev_balance, prev_outstanding\n" +
        "FROM directory\nWHERE " + SummarySearch + "\nORDER BY " + orderBy + "\n" +
        "LIMIT @limit::int OFFSET @offset::int";

    private static string SummaryOrder(string sort, bool descending)
    {
        var dir = descending ? "DESC" : "ASC";
        return sort switch
        {
            "vendorBalance" => "vendor_balance " + dir + ", name, key COLLATE \"C\"",
            "outstandingPayments" => "outstanding_payments " + dir + ", name, key COLLATE \"C\"",
            "name" => "name " + dir + ", key COLLATE \"C\"",
            "code" => "code " + dir + " NULLS LAST, name, key COLLATE \"C\"",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
    }

    // ----- detail (contracts/api.md §2) -----------------------------------------------------------------------

    /// <summary>Vendor header, or null when the key is unknown (the service answers 404).</summary>
    public async Task<FinancialVendorHeader?> ReadVendorAsync(string vendorKey, bool codeUsable, CancellationToken ct)
    {
        await using var cmd = Command("SELECT source_key, name_fa, code FROM mabna.vendor WHERE source_key = @key::text");
        cmd.Parameters.Add(Text("key", vendorKey));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new FinancialVendorHeader(Text(reader, "source_key"), Text(reader, "name_fa"), codeUsable ? Text(reader, "code") : null);
    }

    /// <summary>Detail header totals; every unavailable figure is selected as NULL (no function is called for it).</summary>
    public async Task<FinancialDetailData> ReadDetailAsync(
        string vendorKey, VendorFinancialScope scope,
        bool balance, bool outstanding, bool net, bool payments, CancellationToken ct)
    {
        await using var cmd = Command(DetailSql(balance, outstanding, net, payments, scope));
        cmd.Parameters.Add(Text("key", vendorKey));
        BindDetail(cmd, scope);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new FinancialDetailData(
            VendorBalance: DecOrNothing(reader, "vendor_balance"),
            OutstandingPayments: DecOrNothing(reader, "outstanding_payments"),
            NetPurchases: DecOrNothing(reader, "net_purchases"),
            Payments: DecOrNothing(reader, "payments"),
            PreviousNetPurchases: DecOrNothing(reader, "prev_net"),
            PreviousPayments: DecOrNothing(reader, "prev_payments"));
    }

    private static string DetailSql(bool balance, bool outstanding, bool net, bool payments, VendorFinancialScope scope)
    {
        var netExpr = net
            ? "COALESCE((SELECT SUM(CASE WHEN l.doc_type = 'invoice' THEN l.line_amount ELSE -l.line_amount END) "
              + "FROM mabna.fn_purchase_lines(@fromKey::int, @toKey::int, @basis::text) l "
              + "WHERE l.vendor_source_key = @key::text), 0)"
            : "NULL::numeric";
        var prevNetExpr = net && scope.HasPrevious
            ? "COALESCE((SELECT SUM(CASE WHEN l.doc_type = 'invoice' THEN l.line_amount ELSE -l.line_amount END) "
              + "FROM mabna.fn_purchase_lines(@pFromKey::int, @pToKey::int, @basis::text) l "
              + "WHERE l.vendor_source_key = @key::text), 0)"
            : "NULL::numeric";
        var prevPaymentsExpr = payments && scope.HasPrevious
            ? "mabna.fn_vendor_payments(@pFrom::date, @pTo::date, @key::text)"
            : "NULL::numeric";

        return "SELECT "
            + (balance ? "mabna.fn_vendor_balance(@asOf::date, @key::text)" : "NULL::numeric") + " AS vendor_balance, "
            + (outstanding ? "mabna.fn_outstanding_payments(@asOf::date, @key::text)" : "NULL::numeric") + " AS outstanding_payments, "
            + (payments ? "mabna.fn_vendor_payments(@from::date, @to::date, @key::text)" : "NULL::numeric") + " AS payments, "
            + prevPaymentsExpr + " AS prev_payments, "
            + netExpr + " AS net_purchases, "
            + prevNetExpr + " AS prev_net";
    }

    // ----- history (contracts/api.md §3) ----------------------------------------------------------------------

    public async Task<(long TotalRows, decimal TotalAmount)> HistoryTotalsAsync(
        string vendorKey, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var cmd = Command(
            "SELECT count(*), COALESCE(sum(amount), 0) FROM mabna.vendor_payment " +
            "WHERE NOT cancelled AND paid_at >= @from::date AND paid_at <= @to::date " +
            "AND vendor_source_key = @key::text");
        cmd.Parameters.Add(Text("key", vendorKey));
        cmd.Parameters.Add(Date("from", from));
        cmd.Parameters.Add(Date("to", to));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.GetDecimal(1));
    }

    public async Task<IReadOnlyList<PaymentHistoryData>> ReadHistoryAsync(
        string vendorKey, DateOnly from, DateOnly to, string sort, bool descending, int limit, int offset, CancellationToken ct)
    {
        await using var cmd = Command(
            "SELECT paid_at, reference, instrument_type, amount FROM mabna.vendor_payment " +
            "WHERE NOT cancelled AND paid_at >= @from::date AND paid_at <= @to::date " +
            "AND vendor_source_key = @key::text " +
            "ORDER BY " + HistoryOrder(sort, descending) + "\n" +
            "LIMIT @limit::int OFFSET @offset::int");
        cmd.Parameters.Add(Text("key", vendorKey));
        cmd.Parameters.Add(Date("from", from));
        cmd.Parameters.Add(Date("to", to));
        cmd.Parameters.Add(Int("limit", limit));
        cmd.Parameters.Add(Int("offset", offset));

        var rows = new List<PaymentHistoryData>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new PaymentHistoryData(
                reader.GetDateTime(0) is { } paid ? DateOnly.FromDateTime(paid) : default,
                Text(reader, "reference"),
                Text(reader, "instrument_type") ?? string.Empty,
                Dec(reader, "amount")));
        }

        return rows;
    }

    private static string HistoryOrder(string sort, bool descending)
    {
        var dir = descending ? "DESC" : "ASC";
        return sort switch
        {
            "paidAt" => "paid_at " + dir + ", vendor_payment_id " + dir,
            "amount" => "amount " + dir + ", paid_at DESC, vendor_payment_id DESC",
            "reference" => "reference " + dir + " NULLS LAST, paid_at DESC, vendor_payment_id DESC",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
    }

    // ----- outstanding (contracts/api.md §4) -------------------------------------------------------------------

    public async Task<(long TotalRows, decimal TotalUnpaid)> OutstandingTotalsAsync(
        string vendorKey, DateOnly asOf, CancellationToken ct)
    {
        await using var cmd = Command(
            "SELECT count(*), COALESCE(sum(remaining_amount), 0) FROM mabna.payable_cheque " +
            "WHERE NOT cancelled AND issue_date <= @asOf::date " +
            "AND (cleared_at IS NULL OR cleared_at > @asOf::date) " +
            "AND vendor_source_key = @key::text");
        cmd.Parameters.Add(Text("key", vendorKey));
        cmd.Parameters.Add(Date("asOf", asOf));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.GetDecimal(1));
    }

    public async Task<IReadOnlyList<OutstandingInstrumentData>> ReadOutstandingAsync(
        string vendorKey, DateOnly asOf, string sort, bool descending, int limit, int offset, CancellationToken ct)
    {
        await using var cmd = Command(
            "SELECT issue_date, reference, instrument_type, remaining_amount FROM mabna.payable_cheque " +
            "WHERE NOT cancelled AND issue_date <= @asOf::date " +
            "AND (cleared_at IS NULL OR cleared_at > @asOf::date) " +
            "AND vendor_source_key = @key::text " +
            "ORDER BY " + OutstandingOrder(sort, descending) + "\n" +
            "LIMIT @limit::int OFFSET @offset::int");
        cmd.Parameters.Add(Text("key", vendorKey));
        cmd.Parameters.Add(Date("asOf", asOf));
        cmd.Parameters.Add(Int("limit", limit));
        cmd.Parameters.Add(Int("offset", offset));

        var rows = new List<OutstandingInstrumentData>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new OutstandingInstrumentData(
                reader.GetDateTime(0) is { } issued ? DateOnly.FromDateTime(issued) : default,
                Text(reader, "reference"),
                Text(reader, "instrument_type") ?? string.Empty,
                Dec(reader, "remaining_amount")));
        }

        return rows;
    }

    private static string OutstandingOrder(string sort, bool descending)
    {
        var dir = descending ? "DESC" : "ASC";
        return sort switch
        {
            "date" => "issue_date " + dir + ", payable_cheque_id " + dir,
            "amount" => "remaining_amount " + dir + ", issue_date DESC, payable_cheque_id DESC",
            "reference" => "reference " + dir + " NULLS LAST, issue_date DESC, payable_cheque_id DESC",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
    }

    // ----- shared ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The vendor whose name appears inside a question text (the AI's deterministic vendor scoping, research R9):
    /// the longest vendor name whose search key is contained in the question's search key. Null when the text
    /// names no vendor — the caller then answers unavailable instead of guessing.
    /// </summary>
    public async Task<FinancialVendorHeader?> FindVendorInTextAsync(string? text, CancellationToken ct)
    {
        var normalized = VendorFinancialPeriod.SearchKey(text);
        if (normalized.Length == 0)
        {
            return null;
        }

        await using var cmd = Command(
            "SELECT source_key, name_fa, code FROM mabna.vendor v " +
            "WHERE length(mabna.fn_search_key(v.name_fa)) >= 3 " +
            "AND @q::text LIKE '%' || mabna.fn_search_key(v.name_fa) || '%' ESCAPE '\\' " +
            "ORDER BY length(mabna.fn_search_key(v.name_fa)) DESC LIMIT 1");
        cmd.Parameters.Add(Text("q", normalized));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new FinancialVendorHeader(Text(reader, "source_key"), Text(reader, "name_fa"), Text(reader, "code"));
    }

    /// <summary>Last successful sync time (UTC), or null when none succeeded (quickstart H1).</summary>
    public async Task<DateTimeOffset?> ReadLastSucceededAtAsync(CancellationToken ct)
    {
        await using var cmd = Command(
            "SELECT finished_at FROM sync_job WHERE status = 'succeeded' AND finished_at IS NOT NULL " +
            "ORDER BY finished_at DESC LIMIT 1");
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is DateTime finished
            ? new DateTimeOffset(DateTime.SpecifyKind(finished, DateTimeKind.Utc))
            : null;
    }

    private NpgsqlCommand Command(string sql)
    {
        var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = CommandTimeoutSeconds;
        return cmd;
    }

    internal static decimal Dec(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0m : reader.GetDecimal(ordinal);
    }

    /// <summary>Amount of a gated column: null when the SQL selected NULL (figure unavailable).</summary>
    internal static decimal? DecOrNothing(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    internal static string? Text(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static NpgsqlParameter Int(string name, int value) => new(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter Date(string name, DateOnly value) => new(name, NpgsqlDbType.Date) { Value = value };

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };
}

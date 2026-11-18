namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;

// Response shapes of contracts/api.md §1 (directory), §2 (profile) and §7 (lookups). No DTO here has a
// balance, payment, credit, debt or paid property (FR-012; enforced by NoVendorFinancialsTests).

/// <summary>Vendor header of the profile (contracts/api.md §2): code is null when the code item is unusable.</summary>
public sealed record VendorProfileHeader(string Key, string Name, string? Code);

/// <summary>Profile comparison: previous period net, change and percentage (null when no previous data).</summary>
public sealed record ProfileComparison(string? PreviousNet, string? Delta, string? Percent);

/// <summary>Profile trend bucket: 12 months ending with the period's last month (contracts/api.md §2).</summary>
public sealed record ProfileTrendRow(string Month, string? Net, bool Available, bool Partial);

/// <summary>A product of the profile's top list (at most 10 by net).</summary>
public sealed record ProfileTopProduct(string? Key, string Name, string? Net, string? NetQuantity, bool Unknown);

/// <summary>A product of the profile's returns list.</summary>
public sealed record ProfileReturn(string? ProductKey, string ProductName, string? Returns, string? Quantity, bool Unknown);

/// <summary>The first page of the profile's documents (the documents list with vendor = key).</summary>
public sealed record ProfileDocuments(int Page, int PageSize, long TotalRows, IReadOnlyList<DocumentRow> Rows);

/// <summary>The vendor profile (contracts/api.md §2): the envelope, the vendor, totals, comparison and lists.</summary>
public sealed record VendorProfileResponse(
    string DataLabel,
    ReportSyncTime? LastSuccessfulSync,
    string Currency,
    string CurrencyLabel,
    string DiscountBasis,
    ReportPeriodDto Period,
    ReportPeriodDto? PreviousPeriod,
    IReadOnlyList<ReportFilterEcho> Filters,
    IReadOnlyList<ReportUnavailable> Unavailable,
    VendorProfileHeader Vendor,
    ReportTotals Totals,
    ProfileComparison? Comparison,
    IReadOnlyList<ProfileTrendRow> Trend,
    IReadOnlyList<ProfileTopProduct> TopProducts,
    IReadOnlyList<ProfileReturn> Returns,
    ProfileDocuments Documents,
    DataQualityNotice? DataQuality = null);

/// <summary>A lookup item list for the filter bar (contracts/api.md §7); unavailable only for an unusable source.</summary>
public sealed record LookupResponse(
    IReadOnlyList<LookupItem> Items,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ReportUnavailable>? Unavailable);

/// <summary>
/// The vendor directory, the vendor profile and the filter-bar lookups (spec 004 T031, T042–T043). Every figure
/// reads <c>mabna.fn_purchase_lines</c> through <see cref="PurchasingReportService"/>'s shared envelope and
/// availability; no Mabna access, no user text in SQL (search and keys are bound parameters).
/// </summary>
public sealed class VendorDirectoryService(NpgsqlDataSource dataSource, PurchasingReportService reports)
{
    /// <summary>The profile's own filter declaration: period and the vendor key (comparison always on).</summary>
    public static readonly ReportDefinition ProfileDefinition = new(
        "vendor-profile",
        ReportGrouping.Vendor,
        new[] { ReportFilterNames.Vendor },
        new[] { new SortKey("net", true) },
        SearchTarget.Vendor,
        true,
        true,
        false);

    /// <summary>Report id used for the directory's availability gate and envelope.</summary>
    internal const string DirectoryId = "vendors";

    /// <summary>Report id used for the profile's availability gate and envelope.</summary>
    internal const string ProfileId = "vendor-profile";

    private const int ProfileTopProductLimit = 10;
    private const int ProfileDocumentPageSize = 25;

    /// <summary>The directory page (contracts/api.md §1), with previous-period columns when compare is on.</summary>
    public Task<ReportTablePage<VendorRow>> DirectoryAsync(ReportFilter filter, CancellationToken ct) =>
        PurchasingReportService.Guard(async () =>
        {
            var data = await LoadDirectoryAsync(filter, print: false, ct);
            return new ReportTablePage<VendorRow>(
                data.Envelope.DataLabel, data.Envelope.LastSuccessfulSync, data.Envelope.Currency, data.Envelope.CurrencyLabel,
                data.Envelope.DiscountBasis, data.Envelope.Period, data.Envelope.PreviousPeriod, data.Envelope.Filters,
                data.Envelope.Unavailable, data.Page, filter.PageSize, data.TotalRows, filter.Sort, Dir(filter),
                data.Totals, data.PreviousTotals, data.Rows,
                data.Envelope.DataQuality);
        });

    /// <summary>The directory print twin: the same rows in the current sort, at most 1,000 (research R9).</summary>
    public Task<ReportPrintPage<VendorRow>> DirectoryPrintAsync(ReportFilter filter, CancellationToken ct) =>
        PurchasingReportService.Guard(async () =>
        {
            var data = await LoadDirectoryAsync(filter, print: true, ct);
            var shaped = PrintLimit.Shape(data.Rows, data.TotalRows);
            return new ReportPrintPage<VendorRow>(
                data.Envelope.DataLabel, data.Envelope.LastSuccessfulSync, data.Envelope.Currency, data.Envelope.CurrencyLabel,
                data.Envelope.DiscountBasis, data.Envelope.Period, data.Envelope.PreviousPeriod, data.Envelope.Filters,
                data.Envelope.Unavailable, shaped.TotalRows, shaped.Truncated, shaped.Limit, data.Totals, data.PreviousTotals,
                shaped.Rows,
                data.Envelope.DataQuality);
        });

    /// <summary>
    /// The vendor profile (contracts/api.md §2) for the vendor key in <see cref="ReportFilter.VendorKey"/>; the
    /// comparison is always computed. Null when the key is not in the vendor master (the controller answers 404).
    /// </summary>
    public Task<VendorProfileResponse?> ProfileAsync(ReportFilter filter, CancellationToken ct) =>
        PurchasingReportService.Guard(() => LoadProfileAsync(filter with { Compare = true }, ct));

    /// <summary>Filter-bar lookup (contracts/api.md §7). Null for an unknown kind.</summary>
    public Task<LookupResponse?> LookupAsync(string kind, string? q, int limit, CancellationToken ct) =>
        PurchasingReportService.Guard(() => LoadLookupAsync(kind, q, limit, ct));

    // ----- directory ------------------------------------------------------------------------------------------

    private sealed record DirectoryData(
        ReportEnvelope Envelope,
        long TotalRows,
        int Page,
        ReportTotals Totals,
        ReportTotals? PreviousTotals,
        IReadOnlyList<VendorRow> Rows);

    private async Task<DirectoryData> LoadDirectoryAsync(ReportFilter filter, bool print, CancellationToken ct)
    {
        var context = await reports.ResolveAsync(DirectoryId, filter, ct);
        var envelope = context.Envelope;
        if (context.Empty)
        {
            return new DirectoryData(envelope, 0, 1, new ReportTotals(), null, Array.Empty<VendorRow>());
        }

        var scope = context.Scope;
        var codeUsable = context.VendorCodeAvailable;
        var previous = context.PreviousScope;
        var prevFrom = previous?.FromKey ?? scope.FromKey;
        var prevTo = previous?.ToKey ?? scope.ToKey;

        long totalRows;
        decimal totalNet, totalPrevious;
        await using (var totalsCmd = Command(DirectoryTotalsQuery(codeUsable)))
        {
            BindDirectory(totalsCmd, scope, prevFrom, prevTo);
            await using var reader = await totalsCmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            totalRows = reader.GetInt64(reader.GetOrdinal("total_rows"));
            totalNet = Dec(reader, "net");
            totalPrevious = Dec(reader, "prev_net");
        }

        var page = print ? 1 : ReportFilter.EffectivePage(filter.Page, filter.PageSize, totalRows);
        var (limit, offset) = print ? (PrintLimit.FetchLimit, 0) : (filter.PageSize, (page - 1) * filter.PageSize);

        var rows = new List<VendorRow>();
        await using (var cmd = Command(DirectoryQuery(codeUsable, DirectoryOrder(filter.Sort, filter.Descending))))
        {
            BindDirectory(cmd, scope, prevFrom, prevTo, limit, offset);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var net = Dec(reader, "net");
                var last = reader.IsDBNull(reader.GetOrdinal("last_date")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("last_date"));
                var prevNet = Dec(reader, "prev_net");
                rows.Add(new VendorRow(
                    Key: Text(reader, "key") ?? string.Empty,
                    Name: Text(reader, "name") ?? string.Empty,
                    Code: Text(reader, "code"),
                    Net: ComparisonMath.Amount(net),
                    LastPurchaseDate: last is { } d ? JalaliText(JalaliDate.FromDateTime(d)) : null,
                    LastPurchaseDateJalali: last is { } dj ? JalaliDate.ToPersianDigits(JalaliSlash(JalaliDate.FromDateTime(dj))) : null,
                    PreviousNet: previous is null ? null : ComparisonMath.Amount(prevNet),
                    Delta: previous is null ? null : ComparisonMath.Amount(ComparisonMath.Delta(net, prevNet)),
                    Percent: previous is null ? null : PurchasingReportService.PercentText(net, prevNet)));
            }
        }

        return new DirectoryData(
            envelope,
            totalRows,
            page,
            new ReportTotals(Net: ComparisonMath.Amount(totalNet)),
            previous is null ? null : new ReportTotals(Net: ComparisonMath.Amount(totalPrevious)),
            rows);
    }

    // The directory: every vendor of the master with its period net (0 without purchases), the all-time last
    // invoice date, the previous-period net, and the search over the vendor search key (or the name alone when the
    // code item is unusable, so a code shown as null is not searchable).
    private static string DirectoryCte(bool codeUsable) =>
        @"
WITH net AS (
    SELECT l.vendor_id,
           SUM(CASE WHEN l.doc_type = 'invoice' THEN l.line_amount ELSE 0 END)
             - SUM(CASE WHEN l.doc_type = 'return' THEN l.line_amount ELSE 0 END) AS net
    FROM mabna.fn_purchase_lines(@fromKey::int, @toKey::int, @basis::text) l
    WHERE l.vendor_id IS NOT NULL
    GROUP BY l.vendor_id
),
prev AS (
    SELECT l.vendor_source_key AS key,
           SUM(CASE WHEN l.doc_type = 'invoice' THEN l.line_amount ELSE 0 END)
             - SUM(CASE WHEN l.doc_type = 'return' THEN l.line_amount ELSE 0 END) AS net
    FROM mabna.fn_purchase_lines(@pFromKey::int, @pToKey::int, @basis::text) l
    WHERE l.vendor_id IS NOT NULL
    GROUP BY l.vendor_source_key
),
last_invoice AS (
    SELECT d.vendor_id, MAX(d.doc_date) AS last_date
    FROM mabna.purchase_document d
    WHERE NOT d.cancelled AND d.doc_type = 'invoice' AND d.vendor_id IS NOT NULL
    GROUP BY d.vendor_id
),
directory AS (
    SELECT v.source_key AS key,
           v.name_fa AS name,
           " + (codeUsable ? "v.code" : "NULL::text") + @" AS code,
           " + (codeUsable ? "v.search_key" : "mabna.fn_search_key(v.name_fa)") + @" AS search_text,
           COALESCE(n.net, 0) AS net,
           COALESCE(p.net, 0) AS prev_net,
           li.last_date
    FROM mabna.vendor v
    LEFT JOIN net n ON n.vendor_id = v.vendor_id
    LEFT JOIN prev p ON p.key = v.source_key
    LEFT JOIN last_invoice li ON li.vendor_id = v.vendor_id
)";

    private const string DirectorySearch =
        @"(@q::text IS NULL OR search_text LIKE '%' || mabna.fn_search_key(@q::text) || '%' ESCAPE '\')";

    private static string DirectoryTotalsQuery(bool codeUsable) => DirectoryCte(codeUsable) + @"
SELECT COUNT(*) AS total_rows, COALESCE(SUM(net), 0) AS net, COALESCE(SUM(prev_net), 0) AS prev_net
FROM directory
WHERE " + DirectorySearch;

    private static string DirectoryQuery(bool codeUsable, string orderBy) => DirectoryCte(codeUsable) + @"
SELECT key, name, code, net, prev_net, last_date
FROM directory
WHERE " + DirectorySearch + @"
ORDER BY " + orderBy + @"
LIMIT @limit::int OFFSET @offset::int";

    /// <summary>ORDER BY of the directory: the allowlisted keys only; the key is the final tiebreak.</summary>
    private static string DirectoryOrder(string sort, bool descending)
    {
        var dir = descending ? "DESC" : "ASC";
        return sort switch
        {
            "net" => "net " + dir + ", name, key COLLATE \"C\"",
            "name" => "name " + dir + ", key COLLATE \"C\"",
            "code" => "code " + dir + " NULLS LAST, name, key COLLATE \"C\"",
            "lastPurchase" => "last_date " + dir + " NULLS LAST, name, key COLLATE \"C\"",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };
    }

    private static void BindDirectory(NpgsqlCommand cmd, ReportQueryScope scope, int prevFrom, int prevTo, int? limit = null, int? offset = null)
    {
        cmd.Parameters.Add(Int("fromKey", scope.FromKey));
        cmd.Parameters.Add(Int("toKey", scope.ToKey));
        cmd.Parameters.Add(Int("pFromKey", prevFrom));
        cmd.Parameters.Add(Int("pToKey", prevTo));
        cmd.Parameters.Add(Text("basis", scope.Basis));
        cmd.Parameters.Add(Text("q", scope.Search));
        if (limit is { } l)
        {
            cmd.Parameters.Add(Int("limit", l));
        }

        if (offset is { } o)
        {
            cmd.Parameters.Add(Int("offset", o));
        }
    }

    // ----- profile --------------------------------------------------------------------------------------------

    private async Task<VendorProfileResponse?> LoadProfileAsync(ReportFilter filter, CancellationToken ct)
    {
        var key = filter.VendorKey ?? throw new ArgumentException("vendor key required", nameof(filter));
        var context = await reports.ResolveAsync(ProfileId, filter, ct);
        var envelope = context.Envelope;
        var scope = context.Scope;
        var quantityUsable = context.Figures.Quantity;

        var header = await ReadVendorHeaderAsync(key, context.VendorCodeAvailable, ct);
        if (header is null)
        {
            return null;
        }

        var totals = new ReportTotals();
        ProfileComparison? comparison = null;
        var trend = new List<ProfileTrendRow>();
        var topProducts = new List<ProfileTopProduct>();
        var returns = new List<ProfileReturn>();
        var documents = new ProfileDocuments(1, ProfileDocumentPageSize, 0, Array.Empty<DocumentRow>());

        if (!context.Empty)
        {
            var (_, current) = await reports.ReadGroupedTotalsAsync("by-vendor", scope, ct);
            totals = new ReportTotals(
                Net: current.Net,
                Gross: current.Gross,
                Returns: current.Returns,
                Vat: current.Vat,
                VatInvoice: current.VatInvoice,
                VatReturn: current.VatReturn,
                NetQuantity: quantityUsable ? current.NetQuantity : null,
                DocumentCount: current.DocumentCount);

            if (context.PreviousScope is { } previousScope)
            {
                var (_, previous) = await reports.ReadGroupedTotalsAsync("by-vendor", previousScope, ct);
                var currentNet = ParseAmount(current.Net);
                var previousNet = ParseAmount(previous.Net);
                comparison = new ProfileComparison(
                    previous.Net,
                    ComparisonMath.Amount(ComparisonMath.Delta(currentNet, previousNet)),
                    PurchasingReportService.PercentText(currentNet, previousNet));
            }

            trend.AddRange(await ProfileTrendAsync(filter, scope, ct));

            await using (var topCmd = Command(ReportSql.Grouped("top-products", "net", descending: true, paged: true)))
            {
                ReportSql.Bind(topCmd, scope, ProfileTopProductLimit, 0);
                await using var reader = await topCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var unknown = reader.GetBoolean(reader.GetOrdinal("unknown"));
                    topProducts.Add(new ProfileTopProduct(
                        Key: unknown ? null : Text(reader, "key"),
                        Name: unknown ? PurchasingReportService.UnknownLabel : Text(reader, "name") ?? PurchasingReportService.UnknownLabel,
                        Net: ComparisonMath.Amount(Dec(reader, "net")),
                        NetQuantity: quantityUsable ? ComparisonMath.Amount(Dec(reader, "net_quantity")) : null,
                        Unknown: unknown));
                }
            }

            await using (var returnsCmd = Command(ReportSql.ProfileReturnsByProduct))
            {
                ReportSql.Bind(returnsCmd, scope);
                await using var reader = await returnsCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var unknown = reader.GetBoolean(reader.GetOrdinal("unknown"));
                    returns.Add(new ProfileReturn(
                        ProductKey: unknown ? null : Text(reader, "product_key"),
                        ProductName: unknown ? PurchasingReportService.UnknownLabel : Text(reader, "product_name") ?? PurchasingReportService.UnknownLabel,
                        Returns: ComparisonMath.Amount(Dec(reader, "returns")),
                        Quantity: quantityUsable ? ComparisonMath.Amount(Dec(reader, "quantity")) : null,
                        Unknown: unknown));
                }
            }

            var firstPage = await reports.DocumentsAsync(
                new ReportFilter(filter.Period, false, key, null, null, null, null, null, null, "date", true, 1, ProfileDocumentPageSize, false),
                ct);
            documents = new ProfileDocuments(firstPage.Page, ProfileDocumentPageSize, firstPage.TotalRows, firstPage.Rows);
        }

        return new VendorProfileResponse(
            envelope.DataLabel,
            envelope.LastSuccessfulSync,
            envelope.Currency,
            envelope.CurrencyLabel,
            envelope.DiscountBasis,
            envelope.Period,
            envelope.PreviousPeriod,
            envelope.Filters,
            envelope.Unavailable,
            header,
            totals,
            comparison,
            trend,
            topProducts,
            returns,
            documents,
            envelope.DataQuality);
    }

    /// <summary>12 months ending with the period's last month; a month before the first synced month is not available.</summary>
    private async Task<IReadOnlyList<ProfileTrendRow>> ProfileTrendAsync(ReportFilter filter, ReportQueryScope scope, CancellationToken ct)
    {
        var buckets = filter.Period.TrendMonths(profile: true);
        var window = scope with
        {
            FromKey = PurchasingReportService.KeyOf(buckets[0].Month, 1),
            ToKey = PurchasingReportService.KeyOf(buckets[^1].Month, 31),
        };
        var byMonth = await reports.ReadTrendMonthsAsync(window, ct);
        var firstData = await reports.ReadFirstDataMonthAsync(ct);

        var rows = new List<ProfileTrendRow>(buckets.Count);
        foreach (var bucket in buckets)
        {
            var available = firstData is null || bucket.Month.Index >= firstData.Value.Index;
            var net = byMonth.TryGetValue((bucket.Month.Year, bucket.Month.Month), out var values) ? values.Net : 0m;
            rows.Add(new ProfileTrendRow(bucket.Month.Key, available ? ComparisonMath.Amount(net) : null, available, bucket.Partial));
        }

        return rows;
    }

    private async Task<VendorProfileHeader?> ReadVendorHeaderAsync(string key, bool codeUsable, CancellationToken ct)
    {
        await using var cmd = Command("SELECT source_key, name_fa, code FROM mabna.vendor WHERE source_key = @key::text");
        cmd.Parameters.Add(Text("key", key));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new VendorProfileHeader(
            Text(reader, "source_key") ?? key,
            Text(reader, "name_fa") ?? string.Empty,
            codeUsable ? Text(reader, "code") : null);
    }

    // ----- lookups --------------------------------------------------------------------------------------------

    private async Task<LookupResponse?> LoadLookupAsync(string kind, string? q, int limit, CancellationToken ct)
    {
        var availability = await reports.AvailabilityAsync(ct);
        var search = q is null ? null : ReportFilter.EscapeLike(q);
        var items = new List<LookupItem>();
        IReadOnlyList<ReportUnavailable>? unavailable = null;

        switch (kind)
        {
            case "vendors":
            {
                var codeUsable = Reason(availability, MetricAvailability.FigureVendorCode) is null;
                await ReadLookupAsync(
                    "mabna.vendor",
                    codeUsable ? "search_key" : "mabna.fn_search_key(name_fa)",
                    codeUsable ? "code" : "NULL::text",
                    search,
                    limit,
                    items,
                    ct);
                break;
            }

            case "products":
                await ReadLookupAsync("mabna.product", "search_key", "NULL::text", search, limit, items, ct);
                break;

            case "categories":
            {
                var reason = Reason(availability, MetricAvailability.FigureCategory);
                if (reason is null)
                {
                    await ReadLookupAsync("mabna.product_category", "search_key", "NULL::text", search, limit, items, ct);
                }
                else
                {
                    unavailable = new[] { new ReportUnavailable(MetricAvailability.FigureCategory, reason) };
                }

                break;
            }

            default:
                return null;
        }

        return new LookupResponse(items, unavailable);
    }

    /// <summary>
    /// One lookup query. The table, search and code expressions are fixed text chosen above; the search text and
    /// the limit are bound parameters.
    /// </summary>
    private async Task ReadLookupAsync(string table, string searchExpression, string codeExpression, string? search, int limit, List<LookupItem> items, CancellationToken ct)
    {
        var sql = "SELECT source_key, name_fa, " + codeExpression + " AS code FROM " + table +
                  " WHERE (@q::text IS NULL OR " + searchExpression + " LIKE '%' || mabna.fn_search_key(@q::text) || '%' ESCAPE '\\')" +
                  " ORDER BY name_fa, source_key LIMIT @limit::int";
        await using var cmd = Command(sql);
        cmd.Parameters.Add(Text("q", search));
        cmd.Parameters.Add(Int("limit", limit));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new LookupItem(Text(reader, "source_key"), Text(reader, "name_fa") ?? string.Empty, Text(reader, "code")));
        }
    }

    // ----- helpers --------------------------------------------------------------------------------------------

    private static string? Reason(IReadOnlyDictionary<string, string?> reasons, string figure) =>
        reasons.TryGetValue(figure, out var reason) ? reason : null;

    private NpgsqlCommand Command(string sql)
    {
        var cmd = dataSource.CreateCommand(sql);
        cmd.CommandTimeout = PurchasingReportService.CommandTimeoutSeconds;
        return cmd;
    }

    private static string Dir(ReportFilter filter) => filter.Descending ? "desc" : "asc";

    private static decimal ParseAmount(string? text) =>
        string.IsNullOrEmpty(text) ? 0m : decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static decimal Dec(NpgsqlDataReader reader, string column) => PurchasingReportService.Dec(reader, column);

    private static string? Text(NpgsqlDataReader reader, string column) => PurchasingReportService.Text(reader, column);

    private static NpgsqlParameter Int(string name, int value) => new(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static string JalaliText(JalaliDate date) => $"{date.Year:0000}-{date.Month:00}-{date.Day:00}";

    private static string JalaliSlash(JalaliDate date) => $"{date.Year:0000}/{date.Month:00}/{date.Day:00}";
}

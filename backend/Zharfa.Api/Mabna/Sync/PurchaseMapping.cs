namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Mabna;

/// <summary>Master (vendor or product): the columns that may be read in the current data mode.</summary>
public sealed record MasterSource(
    string Object,
    string Key,
    string? Name,
    string? Active,
    IReadOnlyList<MabnaFilter> Filters,
    ChangeMarker Change,
    IReadOnlyList<string> Columns);

/// <summary>Invoice or return header.</summary>
public sealed record DocumentSource(
    string Object,
    string Key,
    string? Number,
    string? VendorKey,
    string Date,
    string DateFormat,
    string? DocDiscount,
    string? CancelledColumn,
    object? CancelledValue,
    IReadOnlyList<MabnaFilter> Filters,
    ChangeMarker Change,
    IReadOnlyList<string> Columns);

/// <summary>Purchase line. Always read in full (see <see cref="SyncRunner"/>).</summary>
public sealed record LineSource(
    string Object,
    string Key,
    string DocumentKey,
    string? ProductKey,
    string? Quantity,
    string Gross,
    string GrossSign,
    string? LineDiscount,
    string? Vat,
    IReadOnlyList<string> Columns);

/// <summary>
/// The source map resolved for one data mode. A field is used only when its status is usable
/// (production: confirmed/corrected; demo: also candidate). Unusable fields are never read, so
/// an unverified column never reaches a stored value. Required items are checked by
/// <see cref="SourceMapRequirements"/> before any run starts.
/// </summary>
public sealed class PurchaseMapping
{
    public PurchaseMapping(SourceMap map, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(map);
        IsProduction = isProduction;

        // Phase 2 optional items: each is used only when usable; otherwise its figure is unavailable.
        // They are resolved before the masters so that their columns join the master read lists.
        VendorCode = UsableColumn(map.Vendor?.Code);
        ProductCategoryKey = UsableColumn(map.Product?.CategoryKey);

        Vendor = BuildMaster(map.Vendor, "vendor", VendorCode);
        Product = BuildMaster(map.Product, "product", ProductCategoryKey);
        Invoice = BuildDocument(map.PurchaseInvoice, "purchaseInvoice");
        Return = BuildDocument(map.PurchaseReturn, "purchaseReturn");
        Line = BuildLine(map.PurchaseLine);

        ProductCategory = BuildOptionalCategory(map.ProductCategory);
    }

    public bool IsProduction { get; }

    public MasterSource Vendor { get; }

    public MasterSource Product { get; }

    public DocumentSource Invoice { get; }

    public DocumentSource Return { get; }

    public LineSource Line { get; }

    /// <summary>Usable vendor code column (Phase 2), or null when not usable.</summary>
    public string? VendorCode { get; }

    /// <summary>Usable product drug group key column (Phase 2), or null when not usable.</summary>
    public string? ProductCategoryKey { get; }

    /// <summary>Usable product category master (Phase 2), or null when absent or not usable.</summary>
    public MasterSource? ProductCategory { get; }

    /// <summary>Usable column name of a field, or null when the field is absent or not usable.</summary>
    public string? UsableColumn(SourceMapField? field) =>
        SourceMap.IsUsable(field, IsProduction) ? field!.Column : null;

    private MasterSource? BuildOptionalCategory(SourceMapEntity? entity)
    {
        if (entity is null || !SourceMap.IsUsable(entity, IsProduction) ||
            entity.Filter.Any(filter => !SourceMap.IsStatusUsable(filter.Status, IsProduction)))
        {
            return null;
        }

        var key = UsableColumn(entity.Key);
        var name = UsableColumn(entity.Name);
        if (key is null || name is null)
        {
            return null;
        }

        var change = BuildChange(entity);
        return new MasterSource(
            entity.Object!,
            key,
            name,
            null,
            BuildFilters(entity, "productCategory"),
            change,
            Distinct(key, name, ChangeColumnFor(change)));
    }

    /// <param name="optionalColumn">Phase 2 optional column (vendor code or product drug group key), or null when not usable.
    /// It joins the read list so the row mapper receives it; a missing optional column never fails the read.</param>
    private MasterSource BuildMaster(SourceMapEntity? entity, string name, string? optionalColumn)
    {
        RequireUsable(entity, name);
        var key = Require(UsableColumn(entity!.Key), name, "key");
        var label = Require(UsableColumn(entity.Name), name, "name");
        var active = UsableColumn(entity.Active);
        var filters = BuildFilters(entity, name);
        var change = BuildChange(entity);
        return new MasterSource(
            entity.Object!,
            key,
            label,
            active,
            filters,
            change,
            Distinct(key, label, active, ChangeColumnFor(change), optionalColumn));
    }

    private DocumentSource BuildDocument(SourceMapEntity? entity, string name)
    {
        RequireUsable(entity, name);
        var key = Require(UsableColumn(entity!.Key), name, "key");
        var date = entity.Date ?? throw new InvalidOperationException($"{name}.date is missing from the source map.");
        var dateColumn = Require(UsableColumn(date), name, "date");
        var number = UsableColumn(entity.Number);
        var vendorKey = UsableColumn(entity.VendorKey);
        var docDiscount = UsableColumn(entity.DocDiscount);

        string? cancelledColumn = null;
        object? cancelledValue = null;
        var cancelled = entity.Cancelled;
        if (cancelled is not null && SourceMap.IsUsable(cancelled, IsProduction) &&
            string.Equals(cancelled.Rule, "column", StringComparison.Ordinal) &&
            MabnaObjectPolicy.IsValidIdentifier(cancelled.Column))
        {
            cancelledColumn = cancelled.Column;
            cancelledValue = cancelled.Value;
        }

        var filters = BuildFilters(entity, name);
        var change = BuildChange(entity);
        return new DocumentSource(
            entity.Object!,
            key,
            number,
            vendorKey,
            dateColumn,
            date.Format ?? SourceDateParser.JalaliTextFormat,
            docDiscount,
            cancelledColumn,
            cancelledValue,
            filters,
            change,
            Distinct(key, number, vendorKey, dateColumn, docDiscount, cancelledColumn, ChangeColumnFor(change)));
    }

    private LineSource BuildLine(SourceMapEntity? entity)
    {
        RequireUsable(entity, "purchaseLine");
        var key = Require(UsableColumn(entity!.Key), "purchaseLine", "key");
        var documentKey = Require(UsableColumn(entity.DocumentKey), "purchaseLine", "documentKey");
        var gross = entity.GrossAmount ?? throw new InvalidOperationException("purchaseLine.grossAmount is missing from the source map.");
        var grossColumn = Require(UsableColumn(gross), "purchaseLine", "grossAmount");
        var productKey = UsableColumn(entity.ProductKey);
        var quantity = UsableColumn(entity.Quantity);
        var lineDiscount = UsableColumn(entity.LineDiscount);
        var vat = UsableColumn(entity.Vat);
        return new LineSource(
            entity.Object!,
            key,
            documentKey,
            productKey,
            quantity,
            grossColumn,
            gross.AmountSign ?? "asIs",
            lineDiscount,
            vat,
            Distinct(key, documentKey, productKey, quantity, grossColumn, lineDiscount, vat));
    }

    private IReadOnlyList<MabnaFilter> BuildFilters(SourceMapEntity entity, string name)
    {
        var filters = new List<MabnaFilter>();
        foreach (var filter in entity.Filter)
        {
            if (!SourceMap.IsStatusUsable(filter.Status, IsProduction) ||
                !MabnaObjectPolicy.IsValidIdentifier(filter.Column) ||
                !Enum.TryParse<MabnaFilterOp>(filter.Op, ignoreCase: false, out var op))
            {
                throw new InvalidOperationException($"{name} has a filter that is not usable in this data mode.");
            }

            filters.Add(new MabnaFilter(filter.Column!, op, filter.Value));
        }

        return filters;
    }

    private static ChangeMarker BuildChange(SourceMapEntity entity)
    {
        if (entity.Change is null || !SyncVocabulary.TryParseStrategy(entity.Change.Strategy, out var strategy))
        {
            return new ChangeMarker(ChangeStrategy.Full, null);
        }

        return new ChangeMarker(strategy, entity.Change.Column);
    }

    private static string? ChangeColumnFor(ChangeMarker change) =>
        change.Strategy == ChangeStrategy.Full ? null : change.Column;

    private void RequireUsable(SourceMapEntity? entity, string name)
    {
        if (entity is null)
        {
            throw new InvalidOperationException($"{name} is missing from the source map.");
        }

        if (!SourceMap.IsUsable(entity, IsProduction))
        {
            throw new InvalidOperationException($"{name} is not usable in this data mode.");
        }
    }

    private static string Require(string? column, string entity, string item) =>
        column ?? throw new InvalidOperationException($"{entity}.{item} is not usable in this data mode.");

    private static IReadOnlyList<string> Distinct(params string?[] columns) =>
        columns
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>
/// Required items for a sync run (research R2). A missing or unusable required item refuses the
/// run with <c>source-map-invalid</c>; optional items only make dependent figures unavailable.
/// </summary>
public static class SourceMapRequirements
{
    public static IReadOnlyList<string> Problems(SourceMap map, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(map);
        var problems = new List<string>();

        RequireEntity(map.Vendor, "vendor", isProduction, problems, ("key", map.Vendor?.Key), ("name", map.Vendor?.Name));
        RequireEntity(map.Product, "product", isProduction, problems, ("key", map.Product?.Key), ("name", map.Product?.Name));
        RequireDocument(map.PurchaseInvoice, "purchaseInvoice", isProduction, problems);
        RequireDocument(map.PurchaseReturn, "purchaseReturn", isProduction, problems);

        var line = map.PurchaseLine;
        RequireEntity(line, "purchaseLine", isProduction, problems,
            ("key", line?.Key),
            ("documentKey", line?.DocumentKey),
            ("grossAmount", line?.GrossAmount));

        return problems;
    }

    private static void RequireEntity(
        SourceMapEntity? entity,
        string name,
        bool isProduction,
        List<string> problems,
        params (string Item, SourceMapField? Field)[] items)
    {
        if (entity is null)
        {
            problems.Add($"نهاد {name} در نقشه منبع نیست. Entity {name} is missing from the source map.");
            return;
        }

        if (!SourceMap.IsUsable(entity, isProduction))
        {
            problems.Add($"نهاد {name} تأیید نشده یا object ندارد (status={entity.Status ?? "unknown"}). " +
                         $"Entity {name} is not verified or has no object.");
        }

        foreach (var (item, field) in items)
        {
            if (!SourceMap.IsUsable(field, isProduction))
            {
                problems.Add($"{name}.{item} تأیید نشده یا ستون ندارد. {name}.{item} is not verified or has no column.");
            }
        }
    }

    private static void RequireDocument(SourceMapEntity? entity, string name, bool isProduction, List<string> problems)
    {
        RequireEntity(entity, name, isProduction, problems,
            ("key", entity?.Key),
            ("date", entity?.Date));

        if (entity is null)
        {
            return;
        }

        foreach (var filter in entity.Filter)
        {
            if (!SourceMap.IsStatusUsable(filter.Status, isProduction) || string.IsNullOrWhiteSpace(filter.Column))
            {
                problems.Add($"{name}.filter[{filter.Column ?? "?"}] تأیید نشده است. " +
                             $"{name}.filter[{filter.Column ?? "?"}] is not verified.");
            }
        }

        var cancelled = entity.Cancelled;
        if (cancelled is not null && SourceMap.IsUsable(cancelled, isProduction) &&
            (!string.Equals(cancelled.Rule, "column", StringComparison.Ordinal) ||
             !MabnaObjectPolicy.IsValidIdentifier(cancelled.Column)))
        {
            problems.Add($"{name}.cancelled: قاعده تأییدشده فقط rule=column با ستون معتبر را پشتیبانی می‌کند. " +
                         $"{name}.cancelled must use rule=column with a valid column when verified.");
        }
    }
}

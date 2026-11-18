namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Zharfa.Api.Data;

/// <summary>One problem found while loading or validating the source map.</summary>
public sealed record SourceMapProblem(string Code, string Message);

/// <summary>Result of loading a source map file.</summary>
public sealed record SourceMapLoadResult(
    SourceMap? Map,
    bool Configured,
    IReadOnlyList<SourceMapProblem> Problems)
{
    public bool IsValid => Map is not null && Problems.Count == 0;
}

/// <summary>A mapped column with its verification status (contracts/source-map.md).</summary>
public class SourceMapField
{
    public string? Column { get; set; }

    /// <summary>confirmed | corrected | candidate | unknown.</summary>
    public string? Status { get; set; }
}

public sealed class SourceMapDateField : SourceMapField
{
    /// <summary>jalaliText | jalaliInt | gregorian.</summary>
    public string? Format { get; set; }
}

public sealed class SourceMapAmountField : SourceMapField
{
    /// <summary>asIs | negate | abs.</summary>
    public string? AmountSign { get; set; }
}

public sealed class SourceMapCancelledField
{
    /// <summary>column | zeroHeader (header total = 0 and no lines).</summary>
    public string? Rule { get; set; }

    public string? Column { get; set; }

    public object? Value { get; set; }

    public string? Status { get; set; }
}

public sealed class SourceMapFilterField
{
    public string? Column { get; set; }

    /// <summary>A <see cref="MabnaFilterOp"/> name, e.g. Eq.</summary>
    public string? Op { get; set; }

    public object? Value { get; set; }

    public string? Status { get; set; }
}

public sealed class SourceMapChange
{
    /// <summary>rowversion | modifiedAt | idWatermark | full.</summary>
    public string? Strategy { get; set; }

    public string? Column { get; set; }
}

/// <summary>One mapped source entity (vendor, product, invoice, return, line).</summary>
public sealed class SourceMapEntity
{
    public string? Object { get; set; }

    /// <summary>table | view.</summary>
    public string Kind { get; set; } = "table";

    public bool? HostFiltered { get; set; }

    public string? Status { get; set; }

    public List<SourceMapFilterField> Filter { get; } = new();

    public SourceMapField? Key { get; set; }
    public SourceMapField? Name { get; set; }
    public SourceMapField? Active { get; set; }
    public SourceMapField? Number { get; set; }
    public SourceMapField? VendorKey { get; set; }
    public SourceMapField? DocDiscount { get; set; }
    public SourceMapField? DocumentKey { get; set; }
    public SourceMapField? ProductKey { get; set; }
    public SourceMapField? Quantity { get; set; }
    public SourceMapField? LineDiscount { get; set; }
    public SourceMapField? Vat { get; set; }

    /// <summary>Phase 2, optional: the vendor code shown to users (vendor entity only).</summary>
    public SourceMapField? Code { get; set; }

    /// <summary>Phase 2, optional: the drug group key of a product (product entity only).</summary>
    public SourceMapField? CategoryKey { get; set; }

    public SourceMapDateField? Date { get; set; }
    public SourceMapAmountField? GrossAmount { get; set; }
    public SourceMapCancelledField? Cancelled { get; set; }
    public SourceMapChange? Change { get; set; }
}

/// <summary>
/// Model and loader for the gitignored purchasing source map
/// (contracts/source-map.md). The file is supplied on the server via
/// <c>MabnaSync__SourceMapFile</c>; the repository holds only the committed
/// example with made-up names. Loading never throws: malformed JSON and an
/// unknown version come back as <see cref="SourceMapProblem"/>s.
/// </summary>
public sealed class SourceMap
{
    public const int SupportedVersion = 1;

    public int Version { get; private set; }

    /// <summary>Jalali date of the last on-site verification (display only).</summary>
    public string? VerifiedOn { get; private set; }

    public string DiscountBasisValue { get; private set; } = "line";

    public string? DiscountBasisStatus { get; private set; }

    public SourceMapEntity? Vendor { get; private set; }
    public SourceMapEntity? Product { get; private set; }
    public SourceMapEntity? PurchaseInvoice { get; private set; }
    public SourceMapEntity? PurchaseReturn { get; private set; }
    public SourceMapEntity? PurchaseLine { get; private set; }

    /// <summary>Phase 2, optional: the drug group master (product categories).</summary>
    public SourceMapEntity? ProductCategory { get; private set; }

    /// <summary>Loads from <c>MabnaSync__SourceMapFile</c>; unset → not configured.</summary>
    public static SourceMapLoadResult LoadFromFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new SourceMapLoadResult(null, false, Array.Empty<SourceMapProblem>());
        }

        if (!File.Exists(path))
        {
            return new SourceMapLoadResult(null, true, new[]
            {
                new SourceMapProblem("map-missing",
                    "فایل نقشه منبع (MabnaSync__SourceMapFile) پیدا نشد. " +
                    "The configured source map file does not exist.")
            });
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SourceMapLoadResult(null, true, new[]
            {
                new SourceMapProblem("map-unreadable",
                    "فایل نقشه منبع خوانده نشد. The source map file could not be read: " + RedactionHelper.Scrub(ex.Message))
            });
        }

        return Parse(json);
    }

    /// <summary>Parses the JSON text; never throws.</summary>
    public static SourceMapLoadResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new SourceMapLoadResult(null, true, new[]
            {
                new SourceMapProblem("map-malformed",
                    "فایل نقشه منبع معتبر نیست (JSON نادرست). The source map file is not valid JSON.")
            });
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new SourceMapLoadResult(null, true, new[]
                {
                    new SourceMapProblem("map-malformed",
                        "فایل نقشه منبع معتبر نیست (ریشه باید object باشد). The source map root must be a JSON object.")
                });
            }

            if (!root.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version) ||
                version != SupportedVersion)
            {
                return new SourceMapLoadResult(null, true, new[]
                {
                    new SourceMapProblem("version-unknown",
                        $"نسخه نقشه منبع پشتیبانی نمی‌شود (فقط version {SupportedVersion}). " +
                        "Unknown source map version.")
                });
            }

            var map = new SourceMap { Version = version };

            if (root.TryGetProperty("verifiedOn", out var verified) &&
                verified.ValueKind == JsonValueKind.String)
            {
                map.VerifiedOn = verified.GetString();
            }

            if (root.TryGetProperty("discountBasis", out var basis) &&
                basis.ValueKind == JsonValueKind.Object)
            {
                if (basis.TryGetProperty("value", out var basisValue) && basisValue.ValueKind == JsonValueKind.String)
                {
                    map.DiscountBasisValue = basisValue.GetString() ?? "line";
                }

                if (basis.TryGetProperty("status", out var basisStatus) && basisStatus.ValueKind == JsonValueKind.String)
                {
                    map.DiscountBasisStatus = basisStatus.GetString();
                }
            }

            if (!root.TryGetProperty("entities", out var entities) ||
                entities.ValueKind != JsonValueKind.Object)
            {
                return new SourceMapLoadResult(null, true, new[]
                {
                    new SourceMapProblem("entity-missing",
                        "بخش entities در نقشه منبع وجود ندارد. The entities section is missing.")
                });
            }

            map.Vendor = ReadEntity(entities, "vendor");
            map.Product = ReadEntity(entities, "product");
            map.PurchaseInvoice = ReadEntity(entities, "purchaseInvoice");
            map.PurchaseReturn = ReadEntity(entities, "purchaseReturn");
            map.PurchaseLine = ReadEntity(entities, "purchaseLine");
            map.ProductCategory = ReadEntity(entities, "productCategory");

            var problems = new List<SourceMapProblem>();
            if (map.PurchaseInvoice is null)
            {
                problems.Add(new SourceMapProblem("entity-missing",
                    "نهاد purchaseInvoice در نقشه منبع وجود ندارد. The purchaseInvoice entity is required."));
            }

            if (map.PurchaseLine is null)
            {
                problems.Add(new SourceMapProblem("entity-missing",
                    "نهاد purchaseLine در نقشه منبع وجود ندارد. The purchaseLine entity is required."));
            }

            return new SourceMapLoadResult(
                problems.Count == 0 ? map : null,
                true,
                problems);
        }
    }

    /// <summary>
    /// Usability of a status in the given data mode (research R2):
    /// production accepts confirmed/corrected only; demo also accepts candidate.
    /// </summary>
    public static bool IsStatusUsable(string? status, bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var s = status.Trim().ToLowerInvariant();
        if (s is "confirmed" or "corrected")
        {
            return true;
        }

        if (s is "candidate")
        {
            return !isProduction;
        }

        return false; // unknown, or an unrecognized value: unusable, never an error
    }

    /// <summary>An item is usable when it has a column and a usable status.</summary>
    public static bool IsUsable(SourceMapField? field, bool isProduction) =>
        field is not null &&
        !string.IsNullOrWhiteSpace(field.Column) &&
        IsStatusUsable(field.Status, isProduction);

    public static bool IsUsable(SourceMapCancelledField? cancelled, bool isProduction) =>
        cancelled is not null &&
        IsStatusUsable(cancelled.Status, isProduction);

    public static bool IsUsable(SourceMapEntity? entity, bool isProduction) =>
        entity is not null &&
        !string.IsNullOrWhiteSpace(entity.Object) &&
        IsStatusUsable(entity.Status, isProduction);

    // ---- JSON reading helpers ----

    private static SourceMapEntity? ReadEntity(JsonElement entities, string name)
    {
        if (!entities.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var entity = new SourceMapEntity();

        if (TryString(element, "object", out var objectName))
        {
            entity.Object = objectName;
        }

        if (TryString(element, "kind", out var kind) && kind is not null)
        {
            entity.Kind = kind;
        }

        if (element.TryGetProperty("hostFiltered", out var hf))
        {
            entity.HostFiltered = hf.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        if (TryString(element, "status", out var status))
        {
            entity.Status = status;
        }

        if (element.TryGetProperty("filter", out var filter) && filter.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in filter.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var f = new SourceMapFilterField();
                if (TryString(item, "column", out var fc))
                {
                    f.Column = fc;
                }

                if (TryString(item, "op", out var op))
                {
                    f.Op = op;
                }

                if (TryString(item, "status", out var fs))
                {
                    f.Status = fs;
                }

                if (item.TryGetProperty("value", out var fv))
                {
                    f.Value = ReadScalar(fv);
                }

                entity.Filter.Add(f);
            }
        }

        entity.Key = ReadField(element, "key");
        entity.Name = ReadField(element, "name");
        entity.Active = ReadField(element, "active");
        entity.Number = ReadField(element, "number");
        entity.VendorKey = ReadField(element, "vendorKey");
        entity.DocDiscount = ReadField(element, "docDiscount");
        entity.DocumentKey = ReadField(element, "documentKey");
        entity.ProductKey = ReadField(element, "productKey");
        entity.Quantity = ReadField(element, "quantity");
        entity.LineDiscount = ReadField(element, "lineDiscount");
        entity.Vat = ReadField(element, "vat");
        entity.Code = ReadField(element, "code");
        entity.CategoryKey = ReadField(element, "categoryKey");

        if (element.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.Object)
        {
            var df = new SourceMapDateField();
            if (TryString(date, "column", out var dc))
            {
                df.Column = dc;
            }

            if (TryString(date, "format", out var fmt))
            {
                df.Format = fmt;
            }

            if (TryString(date, "status", out var ds))
            {
                df.Status = ds;
            }

            entity.Date = df;
        }

        if (element.TryGetProperty("grossAmount", out var gross) && gross.ValueKind == JsonValueKind.Object)
        {
            var gf = new SourceMapAmountField();
            if (TryString(gross, "column", out var gc))
            {
                gf.Column = gc;
            }

            if (TryString(gross, "amountSign", out var sign))
            {
                gf.AmountSign = sign;
            }

            if (TryString(gross, "status", out var gs))
            {
                gf.Status = gs;
            }

            entity.GrossAmount = gf;
        }

        if (element.TryGetProperty("cancelled", out var cancelled) && cancelled.ValueKind == JsonValueKind.Object)
        {
            var cf = new SourceMapCancelledField();
            if (TryString(cancelled, "rule", out var rule))
            {
                cf.Rule = rule;
            }

            if (TryString(cancelled, "column", out var cc))
            {
                cf.Column = cc;
            }

            if (TryString(cancelled, "status", out var cs))
            {
                cf.Status = cs;
            }

            if (cancelled.TryGetProperty("value", out var cv))
            {
                cf.Value = ReadScalar(cv);
            }

            entity.Cancelled = cf;
        }

        if (element.TryGetProperty("change", out var change) && change.ValueKind == JsonValueKind.Object)
        {
            var ch = new SourceMapChange();
            if (TryString(change, "strategy", out var strategy))
            {
                ch.Strategy = strategy;
            }

            if (TryString(change, "column", out var changeColumn))
            {
                ch.Column = changeColumn;
            }

            // A retired "lookbackDays" key, if a map still carries it, is ignored (research R5).
            entity.Change = ch;
        }

        return entity;
    }

    private static SourceMapField? ReadField(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return new SourceMapField(); // column: null → unusable, not an error
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var field = new SourceMapField();
        if (TryString(element, "column", out var column))
        {
            field.Column = column;
        }

        if (TryString(element, "status", out var status))
        {
            field.Status = status;
        }

        return field;
    }

    /// <summary>
    /// A filter or cancelled value: a string, a decimal number, or null when the
    /// JSON value is neither (or a number outside decimal range). Null makes the
    /// validator report <c>filter-value-invalid</c>; parsing never throws.
    /// </summary>
    private static object? ReadScalar(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Number:
                return value.TryGetDecimal(out var number) ? number : null;
            default:
                return null;
        }
    }

    private static bool TryString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true; // explicit null
        }

        return false;
    }
}

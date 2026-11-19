namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Zharfa.Api.Mabna;

/// <summary>
/// Validates a loaded source map against contracts/source-map.md rules 1–6.
/// Returns a list of problems (Persian message + machine code) and never
/// throws. Items with status <c>unknown</c> or <c>column: null</c> are not
/// errors — they only make dependent metrics unavailable
/// (see <see cref="Purchasing.MetricAvailability"/>).
/// </summary>
public static class SourceMapValidator
{
    private static readonly string[] DateFormats = { "jalaliText", "jalaliInt", "gregorian" };
    private static readonly string[] AmountSigns = { "asIs", "negate", "abs" };
    private static readonly string[] ChangeStrategies = { "rowversion", "modifiedAt", "idWatermark", "full" };

    /// <summary>The candidate balance mixes the code knows (contracts/source-map.md). Anything else is unknown.</summary>
    private static readonly string[] KnownBalanceMixes = { "openPayablesPlusUnpaidCheques" };

    /// <param name="policy">Connector policy used for rule 2 (exclusions + allowlist).</param>
    public static IReadOnlyList<SourceMapProblem> Validate(SourceMap? map, MabnaObjectPolicy policy)
    {
        var problems = new List<SourceMapProblem>();
        if (map is null)
        {
            problems.Add(new SourceMapProblem("map-missing",
                "نقشه منبع بارگذاری نشده است. The source map is not loaded."));
            return problems;
        }

        // Rule 2: object and column identifiers, hard exclusions, allowlist.
        foreach (var (entityName, entity) in Entities(map))
        {
            if (entity is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entity.Object))
            {
                problems.Add(new SourceMapProblem("object-missing",
                    $"نهاد {entityName} object ندارد. Entity {entityName} has no object."));
                continue;
            }

            if (!MabnaObjectPolicy.TryParse(entity.Object, out var objectName, out var parseError))
            {
                problems.Add(new SourceMapProblem("object-invalid",
                    $"نام object نهاد {entityName} نامعتبر است: {parseError}. Invalid object identifier."));
                continue;
            }

            if (MabnaObjectPolicy.IsExcluded(objectName.Name))
            {
                problems.Add(new SourceMapProblem("object-excluded",
                    $"object نهاد {entityName} در فهرست محروم‌شده‌هاست. Object is hard-excluded."));
            }
            else
            {
                try
                {
                    policy.Authorize(entity.Object);
                }
                catch (MabnaAccessDeniedException)
                {
                    problems.Add(new SourceMapProblem("object-not-allowlisted",
                        $"object نهاد {entityName} در allowlist مجاز نیست. Object is not on the connector allowlist."));
                }
            }

            // Rule 3: HOST_ID()-filtered report views are never read live.
            if (string.Equals(entity.Kind, "view", StringComparison.OrdinalIgnoreCase))
            {
                if (entity.HostFiltered is null)
                {
                    problems.Add(new SourceMapProblem("host-filtered-missing",
                        $"kind=view برای {entityName} بدون hostFiltered مجاز نیست. A view must declare hostFiltered explicitly."));
                }
                else if (entity.HostFiltered == true)
                {
                    problems.Add(new SourceMapProblem("host-filtered",
                        $"نهاد {entityName} گزارش فیلترشده با HOST_ID() است و هرگز خوانده نمی‌شود. " +
                        "hostFiltered=true is never read on live Mabna."));
                }
            }
            else if (entity.HostFiltered == true)
            {
                problems.Add(new SourceMapProblem("host-filtered",
                    $"نهاد {entityName} hostFiltered=true دارد. hostFiltered must be false."));
            }

            // Rule 4: filter op/value.
            foreach (var filter in entity.Filter)
            {
                if (string.IsNullOrWhiteSpace(filter.Op) ||
                    !Enum.GetNames<MabnaFilterOp>().Contains(filter.Op))
                {
                    problems.Add(new SourceMapProblem("filter-op-invalid",
                        $"عملگر فیلتر {filter.Op ?? "(خالی)"} برای {entityName} نامعتبر است. " +
                        "filter[].op must be a MabnaFilterOp name."));
                }

                if (filter.Value is null ||
                    filter.Value is not (string or decimal or double or int or long or float))
                {
                    problems.Add(new SourceMapProblem("filter-value-invalid",
                        $"مقدار فیلتر {filter.Column ?? "?"} برای {entityName} باید عدد یا رشته باشد. " +
                        "filter[].value must be a number or string."));
                }
            }

            // Rules 4–5: enum-valued fields.
            if (entity.Date is not null &&
                (string.IsNullOrWhiteSpace(entity.Date.Format) ||
                 !DateFormats.Contains(entity.Date.Format)))
            {
                problems.Add(new SourceMapProblem("date-format-invalid",
                    $"قالب تاریخ '{entity.Date.Format ?? "(خالی)"}' برای {entityName} نامعتبر است. " +
                    "date.format must be jalaliText, jalaliInt or gregorian."));
            }

            if (entity.GrossAmount is not null &&
                (string.IsNullOrWhiteSpace(entity.GrossAmount.AmountSign) ||
                 !AmountSigns.Contains(entity.GrossAmount.AmountSign)))
            {
                problems.Add(new SourceMapProblem("amount-sign-invalid",
                    $"amountSign '{entity.GrossAmount.AmountSign}' برای {entityName} نامعتبر است. " +
                    "amountSign must be asIs, negate or abs."));
            }

            // Phase 6 amount fields (payable remaining is signed, cheque/payment amounts are non-negative).
            if (entity.Amount is not null &&
                (string.IsNullOrWhiteSpace(entity.Amount.AmountSign) ||
                 !AmountSigns.Contains(entity.Amount.AmountSign)))
            {
                problems.Add(new SourceMapProblem("amount-sign-invalid",
                    $"amountSign '{entity.Amount.AmountSign}' برای {entityName} نامعتبر است. " +
                    "amountSign must be asIs, negate or abs."));
            }

            if (entity.RemainingAmount is not null &&
                (string.IsNullOrWhiteSpace(entity.RemainingAmount.AmountSign) ||
                 !AmountSigns.Contains(entity.RemainingAmount.AmountSign)))
            {
                problems.Add(new SourceMapProblem("amount-sign-invalid",
                    $"amountSign '{entity.RemainingAmount.AmountSign}' برای {entityName} نامعتبر است. " +
                    "amountSign must be asIs, negate or abs."));
            }

            if (entity.ClearedAt is not null &&
                (string.IsNullOrWhiteSpace(entity.ClearedAt.Format) ||
                 !DateFormats.Contains(entity.ClearedAt.Format)))
            {
                problems.Add(new SourceMapProblem("date-format-invalid",
                    $"قالب تاریخ '{entity.ClearedAt.Format ?? "(خالی)"}' برای {entityName} نامعتبر است. " +
                    "clearedAt.format must be jalaliText, jalaliInt or gregorian."));
            }

            if (entity.Change is not null &&
                (string.IsNullOrWhiteSpace(entity.Change.Strategy) ||
                 !ChangeStrategies.Contains(entity.Change.Strategy)))
            {
                problems.Add(new SourceMapProblem("change-strategy-invalid",
                    $"استراتژی تغییر '{entity.Change.Strategy ?? "(خالی)"}' برای {entityName} نامعتبر است. " +
                    "change.strategy must be rowversion, modifiedAt, idWatermark or full."));
            }

            // Rule 2: change.column is a column name like every other mapped column.
            if (entity.Change?.Column is not null && !MabnaObjectPolicy.IsValidIdentifier(entity.Change.Column))
            {
                problems.Add(new SourceMapProblem("column-invalid",
                    $"نام ستون تغییر '{entity.Change.Column}' در {entityName} نامعتبر است. Invalid change column identifier."));
            }

            foreach (var (_, field) in NamedFields(entity))
            {
                if (field?.Column is not null && !MabnaObjectPolicy.IsValidIdentifier(field.Column))
                {
                    problems.Add(new SourceMapProblem("column-invalid",
                        $"نام ستون '{field.Column}' در {entityName} نامعتبر است. Invalid column identifier."));
                }
            }
            if (entity.Cancelled?.Column is not null &&
                !MabnaObjectPolicy.IsValidIdentifier(entity.Cancelled.Column))
            {
                problems.Add(new SourceMapProblem("column-invalid",
                    $"نام ستون لغو '{entity.Cancelled.Column}' در {entityName} نامعتبر است. Invalid column identifier."));
            }
        }

        // Rule 6: line linkage.
        var invoice = map.PurchaseInvoice;
        var line = map.PurchaseLine;
        if (line is not null && string.IsNullOrWhiteSpace(line.DocumentKey?.Column))
        {
            problems.Add(new SourceMapProblem("document-key-missing",
                "purchaseLine.documentKey الزامی است. purchaseLine.documentKey is required."));
        }

        // Phase 6 (contracts/source-map.md "Validation additions"): a declared balance formula must name a
        // known mix; absent balanceFormula is not an error (it only makes the balance unavailable).
        if (map.BalanceFormula is not null &&
            (string.IsNullOrWhiteSpace(map.BalanceFormula.Mix) ||
             !KnownBalanceMixes.Contains(map.BalanceFormula.Mix.Trim())))
        {
            problems.Add(new SourceMapProblem("balance-mix-unknown",
                $"مخلوط مانده '{map.BalanceFormula.Mix ?? "(خالی)"}' شناخته نشده است. " +
                "balanceFormula.mix must be a known mix id."));
        }

        // Phase 6: a mapped payableCheque must declare instrumentType and state — either a column or an
        // explicit unknown status — so the formulas know which types/states they cover (FR-001).
        if (map.PayableCheque is not null &&
            (IsRequiredItemMissing(map.PayableCheque.InstrumentType) ||
             IsRequiredItemMissing(map.PayableCheque.State)))
        {
            problems.Add(new SourceMapProblem("payable-cheque-type-state-missing",
                "payableCheque.instrumentType و payableCheque.state باید ستون یا status=unknown صریح داشته باشند. " +
                "payableCheque requires an instrumentType and a state item (column or explicit unknown)."));
        }

        if (invoice is not null && map.PurchaseReturn is not null &&
            !string.IsNullOrWhiteSpace(invoice.Object) &&
            string.Equals(invoice.Object, map.PurchaseReturn.Object, StringComparison.OrdinalIgnoreCase) &&
            SameFilters(invoice.Filter, map.PurchaseReturn.Filter))
        {
            // Same object with identical filters (none counts as identical): invoice and
            // return documents (and therefore their lines) cannot be told apart.
            problems.Add(new SourceMapProblem("line-linkage-ambiguous",
                "invoice و return بدون filter قابل تفکیک نیستند؛ ارتباط خط‌ها مبهم است. " +
                "Invoice and return entities without filters make line linkage ambiguous."));
        }

        return problems;
    }

    /// <summary>
    /// True when both filter lists select the same rows: same column, operator and value,
    /// compared as a multiset (order does not matter). Two empty lists are the same.
    /// </summary>
    private static bool SameFilters(IEnumerable<SourceMapFilterField> left, IEnumerable<SourceMapFilterField> right) =>
        FilterKeys(left).SequenceEqual(FilterKeys(right), StringComparer.Ordinal);

    private static List<string> FilterKeys(IEnumerable<SourceMapFilterField> filters)
    {
        var keys = filters
            .Select(f => string.Join(
                "|",
                (f.Column ?? string.Empty).ToUpperInvariant(),
                (f.Op ?? string.Empty).ToUpperInvariant(),
                Convert.ToString(f.Value, CultureInfo.InvariantCulture) ?? string.Empty))
            .ToList();
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private static IEnumerable<(string Name, SourceMapEntity? Entity)> Entities(SourceMap map)
    {
        yield return ("vendor", map.Vendor);
        yield return ("product", map.Product);
        yield return ("purchaseInvoice", map.PurchaseInvoice);
        yield return ("purchaseReturn", map.PurchaseReturn);
        yield return ("purchaseLine", map.PurchaseLine);
        yield return ("productCategory", map.ProductCategory);
        yield return ("payableDocument", map.PayableDocument);
        yield return ("payableCheque", map.PayableCheque);
        yield return ("vendorPayment", map.VendorPayment);
        yield return ("paymentAllocation", map.PaymentAllocation);
    }

    /// <summary>
    /// A required Phase 6 item is present when it exists and carries either a column or an explicit
    /// <c>unknown</c> status. A missing item, or an item with neither, is invalid for a required formula item.
    /// </summary>
    private static bool IsRequiredItemMissing(SourceMapField? field) =>
        field is null ||
        (string.IsNullOrWhiteSpace(field.Column) &&
         !string.Equals(field.Status?.Trim(), "unknown", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string Name, SourceMapField? Field)> NamedFields(SourceMapEntity entity)
    {
        yield return ("key", entity.Key);
        yield return ("name", entity.Name);
        yield return ("active", entity.Active);
        yield return ("number", entity.Number);
        yield return ("vendorKey", entity.VendorKey);
        yield return ("docDiscount", entity.DocDiscount);
        yield return ("documentKey", entity.DocumentKey);
        yield return ("productKey", entity.ProductKey);
        yield return ("quantity", entity.Quantity);
        yield return ("lineDiscount", entity.LineDiscount);
        yield return ("vat", entity.Vat);
        yield return ("code", entity.Code);
        yield return ("categoryKey", entity.CategoryKey);
        yield return ("amount", entity.Amount);
        yield return ("remainingAmount", entity.RemainingAmount);
        yield return ("instrumentType", entity.InstrumentType);
        yield return ("state", entity.State);
        yield return ("clearedAt", entity.ClearedAt);
        yield return ("instrumentKey", entity.InstrumentKey);
        yield return ("date", entity.Date);
        yield return ("grossAmount", entity.GrossAmount);
        foreach (var filter in entity.Filter)
        {
            yield return ($"filter[{filter.Column}]", new SourceMapField { Column = filter.Column, Status = filter.Status });
        }
    }
}

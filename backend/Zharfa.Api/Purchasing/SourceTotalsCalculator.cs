namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Mabna.Sync;

/// <summary>
/// Purchase totals of one Jalali month, recomputed in C# from mapped source rows (FR-017, research R11).
/// It is the source side of the reconciliation: it applies the definitions in
/// <c>docs/metrics/purchasing.md</c> and shares no SQL with <c>mabna.fn_purchase_totals</c>.
/// Amounts are rounded to 4 decimals as the database stores them. Pure: no I/O.
/// </summary>
/// <param name="Gross">Σ line amount of invoices (gross purchases).</param>
/// <param name="Returns">Σ line amount of returns.</param>
/// <param name="Net">Gross − returns (excluding VAT, by document date).</param>
/// <param name="VatInvoice">Σ VAT of invoices; null when the VAT column is not mapped.</param>
/// <param name="VatReturn">Σ VAT of returns; null when the VAT column is not mapped.</param>
/// <param name="Vat">VatInvoice − VatReturn; null when the VAT column is not mapped.</param>
/// <param name="DocumentCount">Non-cancelled documents of the month that match the vendor filter.</param>
public sealed record PurchaseTotals(
    decimal Gross,
    decimal Returns,
    decimal Net,
    decimal? VatInvoice,
    decimal? VatReturn,
    decimal? Vat,
    int DocumentCount);

public static class SourceTotalsCalculator
{
    public const string InvoiceType = "invoice";
    public const string ReturnType = "return";

    /// <summary>
    /// Computes the totals of one month and filter.
    /// </summary>
    /// <param name="documents">Mapped headers (invoices and returns), in read order; a duplicate key keeps its last row.</param>
    /// <param name="lines">Mapped lines, in read order. A line links to the header with its document key; a line
    /// with no header, or with both an invoice and a return of that key, is not a purchase line (the sync skips it).</param>
    /// <param name="jalaliYear">Jalali year of the period.</param>
    /// <param name="jalaliMonth">Jalali month of the period (1–12).</param>
    /// <param name="vendorSourceKey">Vendor filter on the header, or null for every vendor.</param>
    /// <param name="productSourceKey">Product filter on the line, or null for every product. The discount
    /// allocation of a document always uses all of its lines.</param>
    /// <param name="discountBasis"><c>line</c> or <c>lineAndDocument</c>.</param>
    /// <param name="vatMapped">False when the VAT column is not mapped: the VAT figures are then null.</param>
    public static PurchaseTotals Compute(
        IReadOnlyList<DocumentRecord> documents,
        IReadOnlyList<LineRecord> lines,
        int jalaliYear,
        int jalaliMonth,
        string? vendorSourceKey,
        string? productSourceKey,
        string discountBasis,
        bool vatMapped)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(discountBasis);

        var invoices = new Dictionary<string, DocumentRecord>(StringComparer.Ordinal);
        var returns = new Dictionary<string, DocumentRecord>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (document.DocType == InvoiceType)
            {
                invoices[document.SourceKey] = document;
            }
            else if (document.DocType == ReturnType)
            {
                returns[document.SourceKey] = document;
            }
        }

        var linesByDocument = new Dictionary<string, Dictionary<string, LineRecord>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            // Linked to exactly one header type. Unlinked or ambiguous lines are not purchase lines.
            var isInvoice = invoices.ContainsKey(line.DocumentSourceKey);
            var isReturn = returns.ContainsKey(line.DocumentSourceKey);
            if (isInvoice == isReturn)
            {
                continue;
            }

            if (!linesByDocument.TryGetValue(line.DocumentSourceKey, out var group))
            {
                group = new Dictionary<string, LineRecord>(StringComparer.Ordinal);
                linesByDocument[line.DocumentSourceKey] = group;
            }

            group[line.SourceKey] = line;
        }

        var lineAndDocument = string.Equals(discountBasis.Trim(), PurchasingDiscountBasis.LineAndDocument, StringComparison.OrdinalIgnoreCase);

        decimal gross = 0;
        decimal returned = 0;
        decimal vatInvoice = 0;
        decimal vatReturn = 0;
        var documentCount = 0;

        foreach (var (headers, type) in new[] { (invoices.Values, InvoiceType), (returns.Values, ReturnType) })
        {
            foreach (var header in headers)
            {
                if (!InPeriod(header, jalaliYear, jalaliMonth, vendorSourceKey))
                {
                    continue;
                }

                documentCount++;
                if (!linesByDocument.TryGetValue(header.SourceKey, out var group))
                {
                    continue;
                }

                // Every line of the document takes part in the discount allocation, before the product filter.
                var ordered = group.Values.OrderBy(l => l.SourceKey, StringComparer.Ordinal).ToList();
                var amounts = ordered
                    .Select(l => (Key: l.SourceKey, Amount: Round4(l.GrossAmount) - Round4(l.LineDiscount)))
                    .ToList();
                var shares = lineAndDocument
                    ? DiscountAllocator.ShareOf(amounts, Round4(header.DocDiscount))
                    : Enumerable.Repeat(0m, amounts.Count).ToList();

                for (var i = 0; i < ordered.Count; i++)
                {
                    var line = ordered[i];
                    if (productSourceKey is not null && !string.Equals(line.ProductSourceKey, productSourceKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var lineAmount = amounts[i].Amount - shares[i];
                    var vat = line.VatAmount is null ? 0m : Round4(line.VatAmount.Value);
                    if (type == InvoiceType)
                    {
                        gross += lineAmount;
                        vatInvoice += vat;
                    }
                    else
                    {
                        returned += lineAmount;
                        vatReturn += vat;
                    }
                }
            }
        }

        return new PurchaseTotals(
            Gross: gross,
            Returns: returned,
            Net: gross - returned,
            VatInvoice: vatMapped ? vatInvoice : null,
            VatReturn: vatMapped ? vatReturn : null,
            Vat: vatMapped ? vatInvoice - vatReturn : null,
            DocumentCount: documentCount);
    }

    private static bool InPeriod(DocumentRecord header, int jalaliYear, int jalaliMonth, string? vendorSourceKey) =>
        !header.Cancelled &&
        header.JalaliYear == jalaliYear &&
        header.JalaliMonth == jalaliMonth &&
        (vendorSourceKey is null || string.Equals(header.VendorSourceKey, vendorSourceKey, StringComparison.Ordinal));

    /// <summary>Rounds to 4 decimals, half away from zero, as <c>numeric(24,4)</c> storage does.</summary>
    private static decimal Round4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

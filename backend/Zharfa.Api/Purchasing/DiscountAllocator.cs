namespace Zharfa.Api.Purchasing;

using System;
using System.Collections.Generic;
using System.Numerics;

/// <summary>
/// Pro-rata allocation of a document discount over its lines (FR-014, research R9, data-model.md §4,
/// basis <c>lineAndDocument</c>). Pure: no I/O, no SQL.
///
/// The rules, mirrored exactly by <c>mabna.fn_purchase_line_amounts</c> in <c>0006_purchasing_kpis.sql</c>:
/// <list type="number">
/// <item>Amounts are integers in units of 0.0001 (the scale of <c>numeric(24,4)</c>), so the arithmetic is exact.</item>
/// <item>Each line's share is <c>round(discount × line ÷ Σ lines)</c>, rounded half away from zero, with zero
/// when the discount or the line total is zero.</item>
/// <item>The rounding remainder (discount − Σ shares) goes to the largest line; ties go to the lowest line key
/// (ordinal order). Shares therefore always sum to the document discount exactly.</item>
/// </list>
/// Those rules are independent of the SQL implementation: the reconciliation compares the two.
/// </summary>
public static class DiscountAllocator
{
    /// <summary>Units per Rial: amounts carry 4 decimals.</summary>
    public const int UnitsPerRial = 10000;

    private const decimal UnitsScale = 10000m;

    /// <summary>
    /// Share of the document discount for each line, in the same order as <paramref name="lines"/>.
    /// Each share is a 4-decimal amount; the shares sum to <paramref name="documentDiscount"/> exactly
    /// whenever the document has at least one line.
    /// </summary>
    /// <param name="lines">Line key and line amount (gross − line discount, before the document discount).</param>
    /// <param name="documentDiscount">The header discount, non-negative.</param>
    public static IReadOnlyList<decimal> ShareOf(IReadOnlyList<(string Key, decimal Amount)> lines, decimal documentDiscount)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var shares = new decimal[lines.Count];
        if (lines.Count == 0)
        {
            return shares;
        }

        var discount = ToUnits(documentDiscount);
        if (discount.IsZero)
        {
            return shares;
        }

        var amounts = new BigInteger[lines.Count];
        var total = BigInteger.Zero;
        for (var i = 0; i < lines.Count; i++)
        {
            amounts[i] = ToUnits(lines[i].Amount);
            total += amounts[i];
        }

        var raw = new BigInteger[lines.Count];
        var rawTotal = BigInteger.Zero;
        for (var i = 0; i < lines.Count; i++)
        {
            raw[i] = total.IsZero ? BigInteger.Zero : RoundedRatio(discount * amounts[i], total);
            rawTotal += raw[i];
        }

        var remainder = discount - rawTotal;
        var target = LargestLine(lines, amounts);
        for (var i = 0; i < lines.Count; i++)
        {
            var units = raw[i] + (i == target ? remainder : BigInteger.Zero);
            shares[i] = FromUnits(units);
        }

        return shares;
    }

    /// <summary>Round half away from zero of <paramref name="numerator"/> ÷ <paramref name="denominator"/> (integers).</summary>
    public static BigInteger RoundedRatio(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException();
        }

        var negative = (numerator.Sign < 0) != (denominator.Sign < 0);
        var n = BigInteger.Abs(numerator);
        var d = BigInteger.Abs(denominator);
        var quotient = (2 * n + d) / (2 * d);
        return negative ? -quotient : quotient;
    }

    /// <summary>Index of the largest amount; a tie goes to the lowest key in ordinal order.</summary>
    private static int LargestLine(IReadOnlyList<(string Key, decimal Amount)> lines, BigInteger[] amounts)
    {
        var best = 0;
        for (var i = 1; i < lines.Count; i++)
        {
            var cmp = amounts[i].CompareTo(amounts[best]);
            if (cmp > 0 || (cmp == 0 && string.CompareOrdinal(lines[i].Key, lines[best].Key) < 0))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>Amount as integer units of 0.0001. The input is first rounded to 4 decimals, as the database stores it.</summary>
    public static BigInteger ToUnits(decimal amount)
    {
        var rounded = Math.Round(amount, 4, MidpointRounding.AwayFromZero);
        return new BigInteger(decimal.Truncate(rounded * UnitsScale));
    }

    /// <summary>Integer units of 0.0001 as a 4-decimal amount.</summary>
    public static decimal FromUnits(BigInteger units) => (decimal)units / UnitsScale;
}

namespace Zharfa.Api.Mabna.Sync;

using System;
using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Mabna;

/// <summary>Source of payable documents / credit notes (entity payableDocument); remaining is SIGNED.</summary>
public sealed record PayableDocumentSource(
    string Object,
    string Key,
    string Date,
    string DateFormat,
    string Remaining,
    string RemainingSign,
    string? Number,
    string? VendorKey,
    string? CancelledColumn,
    object? CancelledValue,
    IReadOnlyList<MabnaFilter> Filters,
    IReadOnlyList<string> Columns);

/// <summary>Source of payable cheques (entity payableCheque): type + state + clear date drive the open/closed rule.</summary>
public sealed record PayableChequeSource(
    string Object,
    string Key,
    string IssueDate,
    string IssueDateFormat,
    string Amount,
    string AmountSign,
    string Remaining,
    string RemainingSign,
    string? InstrumentType,
    string? State,
    string? ClearedAt,
    string? ClearedAtFormat,
    string? Reference,
    string? VendorKey,
    string? CancelledColumn,
    object? CancelledValue,
    IReadOnlyList<MabnaFilter> Filters,
    IReadOnlyList<string> Columns);

/// <summary>Source of completed payments (entity vendorPayment): amount is the paid portion only.</summary>
public sealed record VendorPaymentSource(
    string Object,
    string Key,
    string PaidAt,
    string PaidAtFormat,
    string Amount,
    string AmountSign,
    string? InstrumentType,
    string? Reference,
    string? VendorKey,
    string? InstrumentKey,
    string? CancelledColumn,
    object? CancelledValue,
    IReadOnlyList<MabnaFilter> Filters,
    IReadOnlyList<string> Columns);

/// <summary>Source of instrument allocations (entity paymentAllocation), optional.</summary>
public sealed record PaymentAllocationSource(
    string Object,
    string Key,
    string InstrumentKey,
    string VendorKey,
    string Amount,
    string AmountSign,
    IReadOnlyList<string> Columns);

/// <summary>
/// The optional Phase 6 financial entities resolved for one data mode (tasks.md T015, research R6).
/// Each source is null when the entity is absent from the map or not usable in this mode — the sync then skips
/// the table (a purchasing-only install stays complete) and the availability rules report the hole. An entity
/// that IS present must be on the allowlist; <see cref="SourceMapValidator"/> enforces that before any run.
/// </summary>
public sealed class VendorFinancialMapping
{
    public VendorFinancialMapping(SourceMap map, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(map);
        IsProduction = isProduction;

        PayableDocument = BuildDocument(map.PayableDocument);
        PayableCheque = BuildCheque(map.PayableCheque);
        VendorPayment = BuildPayment(map.VendorPayment);
        PaymentAllocation = BuildAllocation(map.PaymentAllocation);
    }

    public bool IsProduction { get; }

    public PayableDocumentSource? PayableDocument { get; }

    public PayableChequeSource? PayableCheque { get; }

    public VendorPaymentSource? VendorPayment { get; }

    public PaymentAllocationSource? PaymentAllocation { get; }

    /// <summary>(logical table, source) pairs to sync in this mode; the runner syncs them in the same transaction.</summary>
    public IEnumerable<(string Table, object Source)> Sources
    {
        get
        {
            if (PayableDocument is { } document)
            {
                yield return (LogicalTables.PayableDocument, document);
            }

            if (PayableCheque is { } cheque)
            {
                yield return (LogicalTables.PayableCheque, cheque);
            }

            if (VendorPayment is { } payment)
            {
                yield return (LogicalTables.VendorPayment, payment);
            }

            if (PaymentAllocation is { } allocation)
            {
                yield return (LogicalTables.PaymentAllocation, allocation);
            }
        }
    }

    private PayableDocumentSource? BuildDocument(SourceMapEntity? entity)
    {
        if (!Usable(entity))
        {
            return null;
        }

        var key = UsableColumn(entity!.Key);
        var date = UsableDate(entity.Date);
        var remaining = UsableAmount(entity.RemainingAmount);
        if (key is null || date is null || remaining is null)
        {
            return null;
        }

        var cancelled = UsableCancelled(entity);
        var filters = BuildFilters(entity);
        if (filters is null)
        {
            return null;
        }

        return new PayableDocumentSource(
            entity.Object!,
            key,
            date.Value.Column,
            date.Value.Format,
            remaining.Value.Column,
            remaining.Value.Sign,
            UsableColumn(entity.Number),
            UsableColumn(entity.VendorKey),
            cancelled.Column,
            cancelled.Value,
            filters,
            ColumnsOf(entity, key, date.Value.Column, remaining.Value.Column,
                UsableColumn(entity.Number), UsableColumn(entity.VendorKey), cancelled.Column));
    }

    private PayableChequeSource? BuildCheque(SourceMapEntity? entity)
    {
        if (!Usable(entity))
        {
            return null;
        }

        var key = UsableColumn(entity!.Key);
        var issue = UsableDate(entity.Date);
        var amount = UsableAmount(entity.Amount);
        var remaining = UsableAmount(entity.RemainingAmount);
        if (key is null || issue is null || amount is null || remaining is null)
        {
            return null;
        }

        var cleared = UsableDate(entity.ClearedAt);
        // clearedAt is optional: absent column = never cleared (the row mapper stores null).
        var cancelled = UsableCancelled(entity);
        var filters = BuildFilters(entity);
        if (filters is null)
        {
            return null;
        }

        return new PayableChequeSource(
            entity.Object!,
            key,
            issue.Value.Column,
            issue.Value.Format,
            amount.Value.Column,
            amount.Value.Sign,
            remaining.Value.Column,
            remaining.Value.Sign,
            UsableColumn(entity.InstrumentType),
            UsableColumn(entity.State),
            cleared?.Column,
            cleared?.Format,
            UsableColumn(entity.Number),
            UsableColumn(entity.VendorKey),
            cancelled.Column,
            cancelled.Value,
            filters,
            ColumnsOf(entity, key, issue.Value.Column, amount.Value.Column, remaining.Value.Column,
                UsableColumn(entity.InstrumentType), UsableColumn(entity.State), cleared?.Column,
                UsableColumn(entity.Number), UsableColumn(entity.VendorKey), cancelled.Column));
    }

    private VendorPaymentSource? BuildPayment(SourceMapEntity? entity)
    {
        if (!Usable(entity))
        {
            return null;
        }

        var key = UsableColumn(entity!.Key);
        var paidAt = UsableDate(entity.Date);
        var amount = UsableAmount(entity.Amount);
        if (key is null || paidAt is null || amount is null)
        {
            return null;
        }

        var cancelled = UsableCancelled(entity);
        var filters = BuildFilters(entity);
        if (filters is null)
        {
            return null;
        }

        return new VendorPaymentSource(
            entity.Object!,
            key,
            paidAt.Value.Column,
            paidAt.Value.Format,
            amount.Value.Column,
            amount.Value.Sign,
            UsableColumn(entity.InstrumentType),
            UsableColumn(entity.Number),
            UsableColumn(entity.VendorKey),
            UsableColumn(entity.InstrumentKey),
            cancelled.Column,
            cancelled.Value,
            filters,
            ColumnsOf(entity, key, paidAt.Value.Column, amount.Value.Column,
                UsableColumn(entity.InstrumentType), UsableColumn(entity.Number),
                UsableColumn(entity.VendorKey), UsableColumn(entity.InstrumentKey), cancelled.Column));
    }

    private PaymentAllocationSource? BuildAllocation(SourceMapEntity? entity)
    {
        if (!Usable(entity))
        {
            return null;
        }

        var key = UsableColumn(entity!.Key);
        var instrumentKey = UsableColumn(entity.InstrumentKey ?? entity.DocumentKey);
        var vendorKey = UsableColumn(entity.VendorKey);
        var amount = UsableAmount(entity.Amount);
        if (key is null || instrumentKey is null || vendorKey is null || amount is null)
        {
            return null;
        }

        return new PaymentAllocationSource(
            entity.Object!,
            key,
            instrumentKey,
            vendorKey,
            amount.Value.Column,
            amount.Value.Sign,
            ColumnsOf(entity, key, instrumentKey, vendorKey, amount.Value.Column));
    }

    private bool Usable(SourceMapEntity? entity) =>
        entity is not null &&
        !string.IsNullOrWhiteSpace(entity.Object) &&
        SourceMap.IsUsable(entity, IsProduction);

    private string? UsableColumn(SourceMapField? field) =>
        SourceMap.IsUsable(field, IsProduction) ? field!.Column : null;

    private (string Column, string Format)? UsableDate(SourceMapDateField? field)
    {
        if (!SourceMap.IsUsable(field, IsProduction) || string.IsNullOrWhiteSpace(field!.Column))
        {
            return null;
        }

        return (field.Column!, field.Format ?? SourceDateParser.JalaliTextFormat);
    }

    private (string Column, string Sign)? UsableAmount(SourceMapAmountField? field)
    {
        if (!SourceMap.IsUsable(field, IsProduction) || string.IsNullOrWhiteSpace(field!.Column))
        {
            return null;
        }

        return (field.Column!, field.AmountSign ?? "asIs");
    }

    /// <summary>The cancelled rule when it is mapped and usable; otherwise (column, value) = (null, null) = not cancelled.</summary>
    private (string? Column, object? Value) UsableCancelled(SourceMapEntity entity)
    {
        var cancelled = entity.Cancelled;
        if (cancelled is null ||
            !SourceMap.IsUsable(cancelled, IsProduction) ||
            !string.Equals(cancelled.Rule, "column", StringComparison.Ordinal) ||
            !MabnaObjectPolicy.IsValidIdentifier(cancelled.Column) ||
            cancelled.Value is null)
        {
            return (null, null);
        }

        return (cancelled.Column, cancelled.Value);
    }

    /// <summary>
    /// The mapped filters when every one is usable in this mode; null when a filter is unusable — the caller then
    /// skips the table (availability reports the hole) instead of reading the wrong rows or failing the run.
    /// </summary>
    private IReadOnlyList<MabnaFilter>? BuildFilters(SourceMapEntity entity)
    {
        var filters = new List<MabnaFilter>();
        foreach (var filter in entity.Filter)
        {
            if (!SourceMap.IsStatusUsable(filter.Status, IsProduction) ||
                !MabnaObjectPolicy.IsValidIdentifier(filter.Column) ||
                !Enum.TryParse<MabnaFilterOp>(filter.Op, ignoreCase: false, out var op))
            {
                return null;
            }

            filters.Add(new MabnaFilter(filter.Column!, op, filter.Value));
        }

        return filters;
    }

    private static IReadOnlyList<string> ColumnsOf(SourceMapEntity entity, params string?[] columns)
    {
        var changeColumn = entity.Change is not null &&
                           SyncVocabulary.TryParseStrategy(entity.Change.Strategy, out var strategy) &&
                           strategy != ChangeStrategy.Full
            ? entity.Change.Column
            : null;

        return columns
            .Append(changeColumn)
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Select(column => column!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

namespace Zharfa.Api.DataQuality;

using System.Collections.Generic;

/// <summary>
/// One validation rule: a single <c>INSERT INTO data_quality_issue … SELECT … ON CONFLICT DO NOTHING</c> over the
/// synced <c>mabna.*</c> copy (data-model §3.2). Reads only; the rules never change a mabna row.
/// </summary>
public sealed record DataQualityRule(string Id, string IssueType, string RecordKind, string Field, string Reason, string Sql);

/// <summary>
/// The rules of data-model §3.2 and the statements that manage a run (§3.1). Every value is a bound parameter
/// (<c>@runId</c>, <c>@todayKey</c>, <c>@earliestKey</c> and the mapped flags); no text from the source reaches SQL.
/// Only non-cancelled documents are checked (product rules PI1 and BR3 do not read documents).
/// </summary>
public static class DataQualitySql
{
    public const string InsertRun =
        "INSERT INTO data_quality_run (sync_job_id, started_at, status) VALUES (@syncJobId, now(), 'running') RETURNING id";

    public const string CompleteRun =
        "UPDATE data_quality_run SET status = 'succeeded', checked_at = now() WHERE id = @runId";

    public const string DeleteOlderIssues =
        "DELETE FROM data_quality_issue WHERE run_id <> @runId";

    public const string InsertFailedRun =
        "INSERT INTO data_quality_run (sync_job_id, started_at, status, error_code) " +
        "VALUES (@syncJobId, now(), 'failed', @errorCode)";

    private const string DocumentRef = "'document:' || d.document_id";
    private const string LineRef = "'line:' || l.line_id";
    private const string ProductRef = "'product:' || p.product_id";

    private const string LineFrom = "mabna.purchase_line l JOIN mabna.purchase_document d ON d.document_id = l.document_id";

    /// <summary>The fifteen rules MV1–MV4, DU1, BR1–BR3, PI1, DT1–DT2, QT1 and AM1–AM3 in the data-model order.</summary>
    public static IReadOnlyList<DataQualityRule> Rules { get; } =
    [
        Document("MV1", "missing-values", "number", "missing",
            "mabna.purchase_document d",
            "NOT d.cancelled AND @numberMapped AND (d.doc_number IS NULL OR btrim(d.doc_number) = '')"),
        Document("MV2", "missing-values", "vendor", "missing",
            "mabna.purchase_document d",
            "NOT d.cancelled AND @vendorMapped AND d.vendor_source_key IS NULL"),
        Line("MV3", "missing-values", "product", "missing",
            "NOT d.cancelled AND @productMapped AND l.product_source_key IS NULL"),
        Line("MV4", "missing-values", "quantity", "missing",
            "NOT d.cancelled AND @quantityMapped AND l.quantity IS NULL"),

        // DU1: the lowest document of each (type, normalized number, vendor) group is kept (owner decision 2026-11-18).
        new DataQualityRule(
            "DU1", "duplicate-records", "document", "number", "duplicate-number",
            Insert("duplicate-records", "document", DocumentRef, "number", "duplicate-number",
                "d.document_id", "NULL::bigint", "NULL::bigint",
                "(SELECT x.document_id, x.keeper FROM (" +
                "SELECT d.document_id, min(d.document_id) OVER (PARTITION BY d.doc_type, d.vendor_source_key, mabna.fn_search_key(d.doc_number)) AS keeper " +
                "FROM mabna.purchase_document d " +
                "WHERE NOT d.cancelled AND btrim(coalesce(d.doc_number, '')) <> '' AND d.vendor_source_key IS NOT NULL" +
                ") x) d",
                "d.document_id <> d.keeper")),

        Document("BR1", "broken-relationships", "vendor", "not-in-master",
            "mabna.purchase_document d",
            "NOT d.cancelled AND d.vendor_source_key IS NOT NULL AND d.vendor_id IS NULL"),
        Line("BR2", "broken-relationships", "product", "not-in-master",
            "NOT d.cancelled AND l.product_source_key IS NOT NULL AND l.product_id IS NULL"),
        new DataQualityRule(
            "BR3", "broken-relationships", "product", "category", "not-in-master",
            Insert("broken-relationships", "product", ProductRef, "category", "not-in-master",
                "NULL::bigint", "NULL::bigint", "p.product_id",
                "mabna.product p",
                "@categoryMapped AND p.drug_group_key IS NOT NULL AND NOT EXISTS (" +
                "SELECT 1 FROM mabna.product_category c WHERE c.source_key = p.drug_group_key)")),

        // PI1: heuristic in Release 1 (labelled on the page): the lowest product of each normalized non-empty name is kept.
        new DataQualityRule(
            "PI1", "inconsistent-product-ids", "product", "name", "duplicate-name",
            Insert("inconsistent-product-ids", "product", ProductRef, "name", "duplicate-name",
                "NULL::bigint", "NULL::bigint", "p.product_id",
                "(SELECT x.product_id, x.keeper FROM (" +
                "SELECT p.product_id, min(p.product_id) OVER (PARTITION BY p.search_key) AS keeper " +
                "FROM mabna.product p WHERE btrim(p.name_fa) <> ''" +
                ") x) p",
                "p.product_id <> p.keeper")),

        Document("DT1", "invalid-dates", "date", "future",
            "mabna.purchase_document d",
            "NOT d.cancelled AND d.jalali_key > @todayKey"),
        Document("DT2", "invalid-dates", "date", "too-early",
            "mabna.purchase_document d",
            "NOT d.cancelled AND d.jalali_key < @earliestKey"),

        Line("QT1", "unexpected-quantities", "quantity", "non-positive",
            "NOT d.cancelled AND l.quantity IS NOT NULL AND l.quantity <= 0"),

        Line("AM1", "inconsistent-amounts", "discount", "discount-exceeds-gross",
            "NOT d.cancelled AND l.line_discount > l.gross_amount"),
        Line("AM2", "inconsistent-amounts", "vat", "negative-vat",
            "NOT d.cancelled AND l.vat_amount < 0"),
        Document("AM3", "inconsistent-amounts", "document-discount", "doc-discount-exceeds-lines",
            "mabna.purchase_document d",
            "NOT d.cancelled " +
            "AND EXISTS (SELECT 1 FROM mabna.purchase_line x WHERE x.document_id = d.document_id) " +
            "AND d.doc_discount > (SELECT sum(x.gross_amount - x.line_discount) FROM mabna.purchase_line x " +
            "WHERE x.document_id = d.document_id) + 1"),
    ];

    private static DataQualityRule Document(string id, string issueType, string field, string reason, string from, string where) =>
        new(id, issueType, "document", field, reason,
            Insert(issueType, "document", DocumentRef, field, reason, "d.document_id", "NULL::bigint", "NULL::bigint", from, where));

    private static DataQualityRule Line(string id, string issueType, string field, string reason, string where) =>
        new(id, issueType, "line", field, reason,
            Insert(issueType, "line", LineRef, field, reason, "d.document_id", "l.line_id", "l.product_id", LineFrom, where));

    private static string Insert(
        string issueType,
        string recordKind,
        string recordRef,
        string field,
        string reason,
        string documentId,
        string lineId,
        string productId,
        string from,
        string where) =>
        "INSERT INTO data_quality_issue (run_id, issue_type, record_kind, record_ref, field, reason, document_id, line_id, product_id) " +
        "SELECT @runId, '" + issueType + "', '" + recordKind + "', " + recordRef + ", '" + field + "', '" + reason + "', " +
        documentId + ", " + lineId + ", " + productId + " " +
        "FROM " + from + " " +
        "WHERE " + where + " " +
        "ON CONFLICT DO NOTHING";
}

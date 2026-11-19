namespace Zharfa.Api.Tests.DataQuality;

using System;
using System.Linq;
using Zharfa.Api.DataQuality;

/// <summary>
/// T076 (data-model §3.2, R11): the fifteen rules, their issue type, record kind, field and reason, the parameters they
/// bind, and the guard rails (synced copy only, non-cancelled documents, no mabna writes). Pure: no database.
/// </summary>
public class DataQualitySqlTests
{
    private static DataQualityRule Rule(string id) => DataQualitySql.Rules.Single(rule => rule.Id == id);

    [Fact]
    public void There_are_the_twenty_two_rule_ids_of_the_data_model()
    {
        var ids = DataQualitySql.Rules.Select(rule => rule.Id).ToArray();

        Assert.Equal(
            new[]
            {
                "MV1", "MV2", "MV3", "MV4", "DU1", "BR1", "BR2", "BR3", "PI1", "DT1", "DT2", "QT1",
                "AM1", "AM2", "AM3",
                // Phase 6 (spec 008): financial rules over the synced payables, cheques and payments.
                "MV5", "BR4", "DT3", "DT4", "DT5", "AM4", "DU2",
            },
            ids);
    }

    [Theory]
    [InlineData("MV1", "missing-values", "document", "number", "missing")]
    [InlineData("MV2", "missing-values", "document", "vendor", "missing")]
    [InlineData("MV3", "missing-values", "line", "product", "missing")]
    [InlineData("MV4", "missing-values", "line", "quantity", "missing")]
    [InlineData("DU1", "duplicate-records", "document", "number", "duplicate-number")]
    [InlineData("BR1", "broken-relationships", "document", "vendor", "not-in-master")]
    [InlineData("BR2", "broken-relationships", "line", "product", "not-in-master")]
    [InlineData("BR3", "broken-relationships", "product", "category", "not-in-master")]
    [InlineData("PI1", "inconsistent-product-ids", "product", "name", "duplicate-name")]
    [InlineData("DT1", "invalid-dates", "document", "date", "future")]
    [InlineData("DT2", "invalid-dates", "document", "date", "too-early")]
    [InlineData("QT1", "unexpected-quantities", "line", "quantity", "non-positive")]
    [InlineData("AM1", "inconsistent-amounts", "line", "discount", "discount-exceeds-gross")]
    [InlineData("AM2", "inconsistent-amounts", "line", "vat", "negative-vat")]
    [InlineData("AM3", "inconsistent-amounts", "document", "document-discount", "doc-discount-exceeds-lines")]
    [InlineData("MV5", "missing-values", "payable", "vendor", "missing")]
    [InlineData("BR4", "broken-relationships", "payable", "vendor", "not-in-master")]
    [InlineData("DT3", "invalid-dates", "payable", "date", "future")]
    [InlineData("DT4", "invalid-dates", "payable", "date", "too-early")]
    [InlineData("DT5", "invalid-dates", "payment", "date", "future")]
    [InlineData("AM4", "inconsistent-amounts", "payable", "amount", "remaining-exceeds-face")]
    [InlineData("DU2", "duplicate-records", "payment", "number", "duplicate-reference")]
    public void Each_rule_has_its_issue_type_kind_field_and_reason(string id, string issueType, string kind, string field, string reason)
    {
        var rule = Rule(id);

        Assert.Equal(issueType, rule.IssueType);
        Assert.Equal(kind, rule.RecordKind);
        Assert.Equal(field, rule.Field);
        Assert.Equal(reason, rule.Reason);
    }

    [Fact]
    public void Every_catalog_issue_type_has_at_least_one_rule()
    {
        foreach (var type in DataQualityCatalog.IssueTypes)
        {
            Assert.Contains(DataQualitySql.Rules, rule => rule.IssueType == type.Id);
        }
    }

    [Fact]
    public void Every_rule_inserts_into_the_issue_table_with_the_run_and_does_nothing_on_conflict()
    {
        foreach (var rule in DataQualitySql.Rules)
        {
            Assert.StartsWith("INSERT INTO data_quality_issue", rule.Sql, StringComparison.Ordinal);
            Assert.Contains("@runId", rule.Sql, StringComparison.Ordinal);
            Assert.Contains("ON CONFLICT DO NOTHING", rule.Sql, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("MV1", "@numberMapped")]
    [InlineData("MV2", "@vendorMapped")]
    [InlineData("MV3", "@productMapped")]
    [InlineData("MV4", "@quantityMapped")]
    [InlineData("BR3", "@categoryMapped")]
    [InlineData("MV5", "@financialVendorMapped")]
    [InlineData("BR4", "@chequeVendorMapped")]
    public void Mapped_rules_bind_their_mapped_flag(string id, string flag)
    {
        Assert.Contains(flag, Rule(id).Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Date_rules_bind_the_sync_day_and_the_earliest_key()
    {
        Assert.Contains("@todayKey", Rule("DT1").Sql, StringComparison.Ordinal);
        Assert.Contains("@earliestKey", Rule("DT2").Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void No_rule_writes_to_the_synced_copy_or_drops_anything()
    {
        string[] forbidden = ["INSERT INTO mabna", "UPDATE mabna", "DELETE FROM mabna", "TRUNCATE", "DROP"];

        foreach (var rule in DataQualitySql.Rules)
        {
            foreach (var text in forbidden)
            {
                Assert.DoesNotContain(text, rule.Sql, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Every_document_rule_except_the_product_rules_skips_cancelled_documents()
    {
        foreach (var rule in DataQualitySql.Rules.Where(rule => rule.Id is not ("PI1" or "BR3")))
        {
            Assert.Contains("NOT d.cancelled", rule.Sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Financial_rules_carry_the_scope_columns_and_no_document_ids()
    {
        foreach (var rule in DataQualitySql.Rules.Where(rule => rule.RecordKind is "payable" or "payment"))
        {
            Assert.Contains("vendor_source_key", rule.Sql, StringComparison.Ordinal);
            Assert.Contains("jalali_key", rule.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("INSERT INTO mabna", rule.Sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Duplicate_rule_groups_by_type_vendor_and_normalized_number()
    {
        var sql = Rule("DU1").Sql;

        Assert.Contains("PARTITION BY d.doc_type, d.vendor_source_key, mabna.fn_search_key(", sql, StringComparison.Ordinal);
        Assert.Contains("vendor_source_key IS NOT NULL", sql, StringComparison.Ordinal);
    }
}

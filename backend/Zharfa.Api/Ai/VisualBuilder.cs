namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Zharfa.Api.Purchasing;
using Zharfa.Api.Purchasing.Reports;
using Zharfa.Api.VendorFinancials;

public sealed record VisualColumnDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("header")] string Header);

public sealed record VisualCaptionsDto(
    [property: JsonPropertyName("period")] string Period,
    [property: JsonPropertyName("filters")] string Filters,
    [property: JsonPropertyName("metrics")] string Metrics);

public sealed record VisualChartSeriesDto(
    [property: JsonPropertyName("metric")] string Metric,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("values")] IReadOnlyList<string?> Values);

public sealed record VisualChartDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("categoryId")] string CategoryId,
    [property: JsonPropertyName("series")] IReadOnlyList<VisualChartSeriesDto> Series);

public sealed record VisualTableDto(
    [property: JsonPropertyName("columns")] IReadOnlyList<VisualColumnDto> Columns,
    [property: JsonPropertyName("rows")] IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

public sealed record SavedReportPeriodSpec(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("from")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From = null,
    [property: JsonPropertyName("to")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To = null,
    [property: JsonPropertyName("month")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Month = null,
    [property: JsonPropertyName("quarter")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Quarter = null,
    [property: JsonPropertyName("year")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Year = null);

public sealed record GeneratedToolSpec(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("sort")] string Sort,
    [property: JsonPropertyName("dir")] string Dir,
    [property: JsonPropertyName("chart")] string Chart,
    [property: JsonPropertyName("metrics")] IReadOnlyList<string> Metrics);

public sealed record VisualSaveSpec(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("period")] SavedReportPeriodSpec Period,
    [property: JsonPropertyName("filters")] IReadOnlyDictionary<string, string?> Filters,
    [property: JsonPropertyName("generated")] GeneratedToolSpec Generated);

public sealed record VisualDto(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("captions")] VisualCaptionsDto Captions,
    [property: JsonPropertyName("table")] VisualTableDto Table,
    [property: JsonPropertyName("chart")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] VisualChartDto? Chart,
    [property: JsonPropertyName("dataQuality")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DataQualityNotice? DataQuality,
    [property: JsonPropertyName("saveSpec")] VisualSaveSpec SaveSpec,
    [property: JsonPropertyName("dataLabel")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DataLabel = null);

/// <summary>
/// Server-side visual builder for ranking and comparison AI answers (spec 007 FR-001..FR-004, contracts/api.md §1).
/// Copies columns and rows from allowlisted tool results without a second query.
/// Pure transformation: no database, no LLM.
/// </summary>
public static class VisualBuilder
{
    public const int MaxRows = 100;

    public static VisualDto? Build(
        string? normalizedQuestion,
        string toolName,
        JalaliPeriod period,
        ToolExecutionResult toolResult,
        string dataLabel)
    {
        // 1. Intent check: if question is present and not list-visual, omit visual
        if (normalizedQuestion != null && !VisualIntent.IsListVisual(normalizedQuestion, toolName))
        {
            return null;
        }

        // 2. If tool failed or row cap exceeded, omit visual
        if (!toolResult.Success || (toolResult.TotalRows > MaxRows && toolResult.RawResult is not ReportTablePage<GroupedRow>))
        {
            return null;
        }

        var columns = new List<VisualColumnDto>();
        var rows = new List<Dictionary<string, string?>>();
        string primaryMetric = "net";
        string primaryMetricLabel = "خالص خرید";
        string metricsCaption = "خالص خرید (ریال)؛ ناخالص، برگشت و مالیات جدا";

        switch (toolName)
        {
            case "purchasing_top_products":
            case "purchasing_by_product":
            {
                columns.Add(new VisualColumnDto("name", "کالا"));
                columns.Add(new VisualColumnDto("net", "خالص خرید"));

                if (toolResult.RawResult is ReportTablePage<GroupedRow> page)
                {
                    foreach (var r in page.Rows)
                    {
                        var dict = new Dictionary<string, string?>
                        {
                            ["key"] = r.Key ?? r.Name,
                            ["name"] = r.Name,
                            ["net"] = r.Net ?? "0.0000",
                            ["gross"] = r.Gross,
                            ["returns"] = r.Returns,
                            ["vat"] = r.Vat
                        };
                        rows.Add(dict);
                    }
                }
                break;
            }

            case "purchasing_by_vendor":
            {
                columns.Add(new VisualColumnDto("name", "تأمین‌کننده"));
                columns.Add(new VisualColumnDto("net", "خالص خرید"));

                if (toolResult.RawResult is ReportTablePage<GroupedRow> page)
                {
                    foreach (var r in page.Rows)
                    {
                        var dict = new Dictionary<string, string?>
                        {
                            ["key"] = r.Key ?? r.Name,
                            ["name"] = r.Name,
                            ["net"] = r.Net ?? "0.0000",
                            ["gross"] = r.Gross,
                            ["returns"] = r.Returns,
                            ["vat"] = r.Vat
                        };
                        rows.Add(dict);
                    }
                }
                break;
            }

            case "purchasing_by_category":
            {
                columns.Add(new VisualColumnDto("name", "گروه کالا"));
                columns.Add(new VisualColumnDto("net", "خالص خرید"));

                if (toolResult.RawResult is ReportTablePage<GroupedRow> page)
                {
                    foreach (var r in page.Rows)
                    {
                        var dict = new Dictionary<string, string?>
                        {
                            ["key"] = r.Key ?? r.Name,
                            ["name"] = r.Name,
                            ["net"] = r.Net ?? "0.0000",
                            ["gross"] = r.Gross,
                            ["returns"] = r.Returns,
                            ["vat"] = r.Vat
                        };
                        rows.Add(dict);
                    }
                }
                break;
            }

            case "purchasing_returns":
            {
                primaryMetric = "returns";
                primaryMetricLabel = "برگشت از خرید";
                metricsCaption = "برگشت از خرید (ریال)";
                columns.Add(new VisualColumnDto("name", "کالا / تأمین‌کننده"));
                columns.Add(new VisualColumnDto("returns", "برگشت از خرید"));

                if (toolResult.RawResult is ReportTablePage<ReturnsRow> page)
                {
                    foreach (var r in page.Rows)
                    {
                        var dict = new Dictionary<string, string?>
                        {
                            ["key"] = $"{r.VendorKey}_{r.ProductKey}",
                            ["name"] = $"{r.VendorName} - {r.ProductName}",
                            ["returns"] = r.Returns ?? "0.0000",
                            ["quantity"] = r.Quantity
                        };
                        rows.Add(dict);
                    }
                }
                break;
            }

            case "purchasing_trend":
            {
                columns.Add(new VisualColumnDto("name", "ماه"));
                columns.Add(new VisualColumnDto("net", "خالص خرید"));

                if (toolResult.RawResult is ReportTablePage<TrendRow> page)
                {
                    foreach (var r in page.Rows)
                    {
                        var dict = new Dictionary<string, string?>
                        {
                            ["key"] = r.Month,
                            ["name"] = r.Label,
                            ["net"] = r.Net ?? "0.0000"
                        };
                        rows.Add(dict);
                    }
                }
                break;
            }

            case "purchasing_overview":
            {
                var isProduct = normalizedQuestion != null &&
                    (normalizedQuestion.Contains("کالا") || normalizedQuestion.Contains("محصول") || normalizedQuestion.Contains("product"));

                if (isProduct)
                {
                    columns.Add(new VisualColumnDto("name", "کالا"));
                    columns.Add(new VisualColumnDto("net", "خالص خرید"));

                    if (toolResult.RawResult is PurchasingOverviewResult ov && ov.TopProducts != null)
                    {
                        foreach (var p in ov.TopProducts)
                        {
                            rows.Add(new Dictionary<string, string?>
                            {
                                ["key"] = p.Name,
                                ["name"] = p.Name,
                                ["net"] = p.Net
                            });
                        }
                    }
                }
                else
                {
                    columns.Add(new VisualColumnDto("name", "تأمین‌کننده"));
                    columns.Add(new VisualColumnDto("net", "خالص خرید"));

                    if (toolResult.RawResult is PurchasingOverviewResult ov && ov.TopVendors != null)
                    {
                        foreach (var v in ov.TopVendors)
                        {
                            rows.Add(new Dictionary<string, string?>
                            {
                                ["key"] = v.Name,
                                ["name"] = v.Name,
                                ["net"] = v.Net
                            });
                        }
                    }
                }
                break;
            }

            case "vendor_financials_list":
            {
                primaryMetric = "vendorBalance";
                primaryMetricLabel = "مانده تأمین‌کننده";
                metricsCaption = "مانده تأمین‌کننده (ریال) و پرداخت‌های معوق";

                columns.Add(new VisualColumnDto("name", "تأمین‌کننده"));
                columns.Add(new VisualColumnDto("vendorBalance", "مانده تأمین‌کننده"));
                columns.Add(new VisualColumnDto("outstandingPayments", "پرداخت‌های معوق"));

                if (toolResult.RawResult is VendorFinancialsSummaryResponse summary)
                {
                    foreach (var r in summary.Rows)
                    {
                        var dict = new Dictionary<string, string?>
                        {
                            ["key"] = r.Key,
                            ["name"] = r.Name,
                            ["vendorBalance"] = r.VendorBalance,
                            ["outstandingPayments"] = r.OutstandingPayments
                        };
                        rows.Add(dict);
                    }
                }
                break;
            }

            default:
                return null;
        }

        if (rows.Count > MaxRows)
        {
            return null;
        }

        // 3. Chart series: bar chart when categories are in [2, 20]
        VisualChartDto? chart = null;
        if (rows.Count >= 2 && rows.Count <= 20 && rows.Any(r => r.GetValueOrDefault(primaryMetric) != null))
        {
            var values = rows.Select(r => r.GetValueOrDefault(primaryMetric)).ToArray();
            chart = new VisualChartDto(
                Type: "bar",
                CategoryId: "name",
                Series: [new VisualChartSeriesDto(primaryMetric, primaryMetricLabel, values)]);
        }

        // 4. Captions
        var filterParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(toolResult.Vendor)) filterParts.Add($"تأمین‌کننده: {toolResult.Vendor}");
        if (!string.IsNullOrWhiteSpace(toolResult.Product)) filterParts.Add($"کالا: {toolResult.Product}");
        if (!string.IsNullOrWhiteSpace(toolResult.Category)) filterParts.Add($"گروه: {toolResult.Category}");
        var filtersText = filterParts.Count > 0 ? string.Join("، ", filterParts) : "بدون فیلتر";

        var captions = new VisualCaptionsDto(
            Period: period.Label,
            Filters: filtersText,
            Metrics: metricsCaption);

        // 5. Save spec for Keep
        SavedReportPeriodSpec periodSpec = period.Kind switch
        {
            JalaliPeriodKind.Custom => new SavedReportPeriodSpec("custom", From: period.FromWire, To: period.ToWire),
            JalaliPeriodKind.Month => new SavedReportPeriodSpec("month", Month: period.StartMonth.Key),
            JalaliPeriodKind.Quarter => new SavedReportPeriodSpec("quarter", Quarter: $"{period.From.Year}-Q{(period.From.Month - 1) / 3 + 1}"),
            JalaliPeriodKind.Year => new SavedReportPeriodSpec("year", Year: period.From.Year.ToString()),
            _ => new SavedReportPeriodSpec("custom", From: period.FromWire, To: period.ToWire)
        };

        var saveFilters = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(toolResult.Vendor)) saveFilters["vendor"] = toolResult.Vendor;
        if (!string.IsNullOrWhiteSpace(toolResult.Product)) saveFilters["product"] = toolResult.Product;
        if (!string.IsNullOrWhiteSpace(toolResult.Category)) saveFilters["category"] = toolResult.Category;

        var generatedSpec = new GeneratedToolSpec(
            Tool: toolName,
            Limit: Math.Min(Math.Max(rows.Count, 10), MaxRows),
            Sort: primaryMetric,
            Dir: "desc",
            Chart: "bar",
            Metrics: ["net", "gross", "returns", "vat"]);

        var saveSpec = new VisualSaveSpec("generated", periodSpec, saveFilters, generatedSpec);

        var dataQuality = toolResult.DataQuality is { AffectedRecords: > 0 } dq ? dq : null;

        return new VisualDto(
            Kind: "generated",
            Captions: captions,
            Table: new VisualTableDto(columns, rows),
            Chart: chart,
            DataQuality: dataQuality,
            SaveSpec: saveSpec,
            DataLabel: dataLabel);
    }
}

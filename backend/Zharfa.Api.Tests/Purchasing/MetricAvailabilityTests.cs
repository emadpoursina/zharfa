namespace Zharfa.Api.Tests.Purchasing;

using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Purchasing;

public class MetricAvailabilityTests
{
    private static readonly string ExampleMapPath =
        Path.Combine(AppContext.BaseDirectory, "example", "purchase-source-map.example.json");

    private static SourceMap Map() => SourceMap.LoadFromFile(ExampleMapPath).Map!;

    private static IReadOnlyDictionary<string, string?> Evaluate(
        SourceMap? map, bool isProduction, bool hasSync) =>
        MetricAvailability.Evaluate(map, isProduction, hasSync);

    [Fact]
    public void No_successful_sync_makes_every_figure_unavailable()
    {
        var result = Evaluate(Map(), isProduction: false, hasSync: false);
        foreach (var figure in new[]
                 {
                     MetricAvailability.FigureNet, MetricAvailability.FigureGross,
                     MetricAvailability.FigureReturns, MetricAvailability.FigureVat,
                     MetricAvailability.FigureActiveVendors, MetricAvailability.FigureTopVendors,
                     MetricAvailability.FigureTopProducts, MetricAvailability.FigureTrend,
                     MetricAvailability.FigureComparison
                 })
        {
            Assert.Equal(MetricAvailability.NoSuccessfulSync, result[figure]);
        }
    }

    [Fact]
    public void Demo_with_candidate_map_and_sync_has_all_figures_available()
    {
        var result = Evaluate(Map(), isProduction: false, hasSync: true);
        Assert.All(result.Values, reason => Assert.Null(reason));
    }

    [Fact]
    public void Production_with_candidate_map_makes_all_dependent_figures_unavailable()
    {
        // The example map is all-candidate: nothing is verified for production.
        var result = Evaluate(Map(), isProduction: true, hasSync: true);
        foreach (var figure in new[]
                 {
                     MetricAvailability.FigureNet, MetricAvailability.FigureGross,
                     MetricAvailability.FigureReturns, MetricAvailability.FigureTopVendors,
                     MetricAvailability.FigureTopProducts, MetricAvailability.FigureTrend,
                     MetricAvailability.FigureComparison, MetricAvailability.FigureActiveVendors
                 })
        {
            Assert.Equal(MetricAvailability.SourceItemUnverified, result[figure]);
        }
        // vat column exists but its status is candidate → unverified
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureVat]);
    }

    [Fact]
    public void Unmapped_VAT_column_reports_source_item_unmapped()
    {
        var map = Map();
        map.PurchaseLine!.Vat!.Column = null;
        var result = Evaluate(map, isProduction: false, hasSync: true);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureVat]);
        // VAT only affects the VAT figure
        Assert.Null(result[MetricAvailability.FigureNet]);
        Assert.Null(result[MetricAvailability.FigureGross]);
    }

    [Fact]
    public void Unusable_VAT_status_reports_source_item_unverified()
    {
        var map = Map();
        map.PurchaseLine!.Vat!.Status = "unknown";
        var result = Evaluate(map, isProduction: false, hasSync: true);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureVat]);
    }

    [Fact]
    public void Unusable_invoice_date_blocks_gross_net_trend_but_not_returns()
    {
        var map = Map();
        map.PurchaseInvoice!.Date!.Status = "unknown";
        var result = Evaluate(map, isProduction: false, hasSync: true);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureGross]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureNet]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureTrend]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureComparison]);
        Assert.Null(result[MetricAvailability.FigureReturns]);
        Assert.Null(result[MetricAvailability.FigureVat]);
    }

    [Fact]
    public void Unusable_return_header_blocks_returns_and_net_not_gross()
    {
        var map = Map();
        map.PurchaseReturn!.Key!.Status = "unknown";
        var result = Evaluate(map, isProduction: false, hasSync: true);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureReturns]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureNet]);
        Assert.Null(result[MetricAvailability.FigureGross]);
    }

    [Fact]
    public void Missing_vendor_key_blocks_activeVendors_and_topVendors_only()
    {
        var map = Map();
        map.PurchaseInvoice!.VendorKey = null;
        var result = Evaluate(map, isProduction: false, hasSync: true);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureActiveVendors]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureTopVendors]);
        Assert.Null(result[MetricAvailability.FigureNet]);
        Assert.Null(result[MetricAvailability.FigureTopProducts]);
    }

    [Fact]
    public void Missing_product_key_blocks_topProducts_only()
    {
        var map = Map();
        map.PurchaseLine!.ProductKey = null;
        var result = Evaluate(map, isProduction: false, hasSync: true);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureTopProducts]);
        Assert.Null(result[MetricAvailability.FigureNet]);
        Assert.Null(result[MetricAvailability.FigureActiveVendors]);
    }

    [Fact]
    public void Missing_map_means_everything_unmapped()
    {
        var result = Evaluate(null, isProduction: false, hasSync: true);
        Assert.All(result.Values, reason =>
            Assert.Equal(MetricAvailability.SourceItemUnmapped, reason));
    }

    [Fact]
    public void Unusable_doc_type_filter_blocks_invoice_side()
    {
        var map = Map();
        map.PurchaseInvoice!.Filter[0].Status = "unknown";
        var result = Evaluate(map, isProduction: false, hasSync: true);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureGross]);
        Assert.Null(result[MetricAvailability.FigureReturns]);
    }

    // ---- T083: cancelled rule (FR-001, FR-027) ----

    private static readonly string[] AllFiguresForCancelled =
    {
        MetricAvailability.FigureNet, MetricAvailability.FigureGross,
        MetricAvailability.FigureReturns, MetricAvailability.FigureVat,
        MetricAvailability.FigureActiveVendors, MetricAvailability.FigureTopVendors,
        MetricAvailability.FigureTopProducts, MetricAvailability.FigureTrend,
        MetricAvailability.FigureComparison
    };

    [Fact]
    public void Invoice_without_cancelled_rule_makes_every_figure_that_uses_invoices_unmapped()
    {
        var map = Map();
        map.PurchaseInvoice!.Cancelled = null;
        var result = Evaluate(map, isProduction: false, hasSync: true);

        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureGross]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureNet]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureVat]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureActiveVendors]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureTrend]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureComparison]);
        // Returns depend only on the return header's cancelled rule.
        Assert.Null(result[MetricAvailability.FigureReturns]);
    }

    [Fact]
    public void Unusable_cancelled_status_on_return_blocks_returns_and_combined_figures_but_not_gross()
    {
        var map = Map();
        map.PurchaseReturn!.Cancelled!.Status = "unknown";
        var result = Evaluate(map, isProduction: false, hasSync: true);

        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureReturns]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureNet]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureVat]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureActiveVendors]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureTopVendors]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureTopProducts]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureTrend]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureComparison]);
        // Gross uses only invoices, so its rule is the invoice's, which is usable here.
        Assert.Null(result[MetricAvailability.FigureGross]);
    }

    [Fact]
    public void Column_rule_without_column_is_unmapped()
    {
        var map = Map();
        map.PurchaseInvoice!.Cancelled!.Column = null;
        var result = Evaluate(map, isProduction: false, hasSync: true);

        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureGross]);
    }

    [Fact]
    public void Unknown_cancelled_rule_is_unmapped()
    {
        var map = Map();
        map.PurchaseInvoice!.Cancelled!.Rule = "flagInAnotherTable";
        var result = Evaluate(map, isProduction: false, hasSync: true);

        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureGross]);
    }

    [Fact]
    public void Zero_header_cancelled_rule_needs_no_column_and_is_available_when_usable()
    {
        var map = Map();
        map.PurchaseInvoice!.Cancelled = new SourceMapCancelledField { Rule = "zeroHeader", Status = "confirmed" };
        map.PurchaseReturn!.Cancelled = new SourceMapCancelledField { Rule = "zeroHeader", Status = "confirmed" };
        var result = Evaluate(map, isProduction: false, hasSync: true);

        Assert.All(AllFiguresForCancelled, figure => Assert.Null(result[figure]));
    }

    [Fact]
    public void Candidate_cancelled_rule_is_unverified_in_production()
    {
        var result = Evaluate(Map(), isProduction: true, hasSync: true);

        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureGross]);
    }

    // ---- T083: docDiscount under basis lineAndDocument (FR-014, FR-027) ----

    [Fact]
    public void Line_basis_does_not_require_docDiscount()
    {
        var result = MetricAvailability.Evaluate(Map(), isProduction: false, hasSucceededSync: true, discountBasis: "line");

        Assert.All(result.Values, reason => Assert.Null(reason));
    }

    [Fact]
    public void LineAndDocument_with_unmapped_docDiscount_makes_line_amount_figures_unmapped()
    {
        var result = MetricAvailability.Evaluate(
            Map(), isProduction: false, hasSucceededSync: true, discountBasis: "lineAndDocument");

        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureGross]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureReturns]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureNet]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureTopVendors]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureTopProducts]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureTrend]);
        Assert.Equal(MetricAvailability.SourceItemUnmapped, result[MetricAvailability.FigureComparison]);
        // Figures that do not use line amounts stay available.
        Assert.Null(result[MetricAvailability.FigureVat]);
        Assert.Null(result[MetricAvailability.FigureActiveVendors]);
    }

    [Fact]
    public void LineAndDocument_with_mapped_docDiscount_is_available_in_demo()
    {
        var map = Map();
        map.PurchaseInvoice!.DocDiscount = new SourceMapField { Column = "DocDisc", Status = "confirmed" };
        map.PurchaseReturn!.DocDiscount = new SourceMapField { Column = "DocDisc", Status = "confirmed" };

        var result = MetricAvailability.Evaluate(map, isProduction: false, hasSucceededSync: true, discountBasis: "lineAndDocument");

        Assert.All(result.Values, reason => Assert.Null(reason));
    }

    [Fact]
    public void LineAndDocument_with_candidate_docDiscount_is_unverified_in_production()
    {
        var map = Map();
        map.PurchaseInvoice!.DocDiscount = new SourceMapField { Column = "DocDisc", Status = "candidate" };

        var result = MetricAvailability.Evaluate(map, isProduction: true, hasSucceededSync: true, discountBasis: "lineAndDocument");

        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureGross]);
        Assert.Equal(MetricAvailability.SourceItemUnverified, result[MetricAvailability.FigureNet]);
    }
}

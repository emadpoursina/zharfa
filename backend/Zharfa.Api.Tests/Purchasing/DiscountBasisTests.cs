namespace Zharfa.Api.Tests.Purchasing;

using Zharfa.Api.Purchasing;

public class DiscountBasisTests
{
    [Theory]
    [InlineData("line")]
    [InlineData("lineAndDocument")]
    [InlineData("LINE")]
    public void Known_basis_is_accepted_in_demo_without_a_map(string configured)
    {
        Assert.Null(PurchasingDiscountBasis.Validate(configured, isProduction: false, verifiedBasis: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("gross")]
    public void Unknown_basis_is_refused_in_every_mode(string configured)
    {
        Assert.NotNull(PurchasingDiscountBasis.Validate(configured, isProduction: false, verifiedBasis: null));
        Assert.NotNull(PurchasingDiscountBasis.Validate(configured, isProduction: true, verifiedBasis: "line"));
    }

    [Fact]
    public void Production_refuses_a_basis_that_differs_from_the_verified_map_basis()
    {
        var error = PurchasingDiscountBasis.Validate("line", isProduction: true, verifiedBasis: "lineAndDocument");
        Assert.NotNull(error);
        Assert.Contains("Purchasing__DiscountBasis", error);
    }

    [Fact]
    public void Production_accepts_a_basis_equal_to_the_verified_map_basis()
    {
        Assert.Null(PurchasingDiscountBasis.Validate("lineAndDocument", isProduction: true, verifiedBasis: "lineAndDocument"));
    }

    [Fact]
    public void Production_without_a_loaded_map_has_nothing_to_compare()
    {
        Assert.Null(PurchasingDiscountBasis.Validate("line", isProduction: true, verifiedBasis: null));
    }

    [Fact]
    public void Demo_does_not_refuse_on_a_mismatch()
    {
        Assert.Null(PurchasingDiscountBasis.Validate("line", isProduction: false, verifiedBasis: "lineAndDocument"));
    }

    [Fact]
    public void Empty_configured_value_falls_back_to_the_default_basis()
    {
        var options = new PurchasingOptions { DiscountBasis = "   " };
        Assert.Equal(PurchasingOptions.DefaultDiscountBasis, options.DiscountBasis);
        Assert.Null(PurchasingDiscountBasis.Validate(options.DiscountBasis, isProduction: false, verifiedBasis: null));
    }
}

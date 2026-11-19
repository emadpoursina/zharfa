namespace Zharfa.Api.Tests.SavedReports;

using Zharfa.Api.SavedReports;

/// <summary>
/// T005 (spec 007 US2, US6, research R7): SavedReportName validation, normalization and 50-cap rules.
/// Pure: no database, no network.
/// </summary>
public class SavedReportNameTests
{
    [Fact]
    public void Cap_constant_is_fifty()
    {
        Assert.Equal(50, SavedReportName.MaxPerUser);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Empty_or_whitespace_name_is_invalid(string? raw)
    {
        var ok = SavedReportName.TryValidate(raw, out var trimmed, out var nameKey, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, nameKey);
        Assert.Equal(SavedReportName.EmptyNameMessage, error);
    }

    [Fact]
    public void Valid_name_trims_and_normalizes_persian_letters()
    {
        var ok = SavedReportName.TryValidate("  گزارش خرید آبان  ", out var trimmed, out var nameKey, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("گزارش خرید آبان", trimmed);
        Assert.Equal("گزارش خرید آبان", nameKey);
    }

    [Fact]
    public void Arabic_kaf_and_persian_kaf_collide_after_normalization()
    {
        // " ده کالا " with Persian ک vs "ده كالا" with Arabic ك (\u0643)
        var key1 = SavedReportName.NormalizeKey(" ده کالا ");
        var key2 = SavedReportName.NormalizeKey("ده كالا");

        Assert.Equal("ده کالا", key1);
        Assert.Equal("ده کالا", key2);
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Arabic_yeh_and_persian_yeh_collide_after_normalization()
    {
        // Persian ی (\u06CC) vs Arabic ي (\u064A)
        var key1 = SavedReportName.NormalizeKey("خرید نقدی");
        var key2 = SavedReportName.NormalizeKey("خريد نقدي");

        Assert.Equal(key1, key2);
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(49, false, true)]
    [InlineData(50, false, false)]
    [InlineData(51, false, false)]
    [InlineData(50, true, true)]
    [InlineData(51, true, true)]
    public void CanInsert_respects_the_cap_and_allows_overwrite(int count, bool isOverwrite, bool expected)
    {
        Assert.Equal(expected, SavedReportName.CanInsert(count, isOverwrite));
    }
}

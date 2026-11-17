namespace Zharfa.Api.Tests.Sync;

using Zharfa.Api.Mabna.Sync;

public class PersianTextTests
{
    [Fact]
    public void Arabic_yeh_becomes_Persian_yeh()
    {
        Assert.Equal("شرکت دارویی", PersianText.Normalize("شركت دارويي"));
        Assert.Equal("یاس", PersianText.Normalize("ياس"));
    }

    [Fact]
    public void Arabic_kaf_becomes_Persian_kaf()
    {
        Assert.Equal("کالا", PersianText.Normalize("كالا"));
    }

    [Fact]
    public void Names_differing_only_by_yeh_or_kaf_compare_equal()
    {
        Assert.Equal(PersianText.Normalize("ياس"), PersianText.Normalize("یاس"));
        Assert.Equal(PersianText.Normalize("كالا"), PersianText.Normalize("کالا"));
        Assert.Equal(PersianText.Normalize("داروخانه يکم"), PersianText.Normalize("داروخانه یکم"));
    }

    [Fact]
    public void ZWNJ_is_kept()
    {
        Assert.Equal("می\u200Cشود", PersianText.Normalize("می\u200Cشود"));
    }

    [Fact]
    public void Internal_whitespace_is_collapsed_and_edges_trimmed()
    {
        Assert.Equal("شرکت  نمونه".Replace("  ", " "), PersianText.Normalize("  شرکت   نمونه  "));
        Assert.Equal("الف", PersianText.Normalize("\t الف \n"));
    }

    [Fact]
    public void Digits_are_kept_as_is()
    {
        Assert.Equal("دارو 123 ۴۵۶", PersianText.Normalize("دارو 123 ۴۵۶"));
    }

    [Fact]
    public void Empty_input_returns_empty_and_placeholder_helper_substitutes()
    {
        Assert.Equal(string.Empty, PersianText.Normalize(null));
        Assert.Equal(string.Empty, PersianText.Normalize("   "));
        Assert.Equal(PersianText.EmptyNamePlaceholder, PersianText.NormalizeName(null));
        Assert.Equal(PersianText.EmptyNamePlaceholder, PersianText.NormalizeName("   "));
        Assert.Equal("نام", PersianText.NormalizeName("نام"));
    }

    [Fact]
    public void Null_chars_and_control_characters_do_not_crash()
    {
        Assert.Equal("abc", PersianText.Normalize("  abc  "));
    }
}

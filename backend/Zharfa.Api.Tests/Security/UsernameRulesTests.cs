namespace Zharfa.Api.Tests.Security;

using Zharfa.Api.Security;

/// <summary>
/// T030 (data-model §1.1, research R4): username keys (ی/ک and ي/ك equality, case folding, trimming) and the
/// length and character rules with Persian error texts. Pure: no database, no network.
/// </summary>
public class UsernameRulesTests
{
    [Fact]
    public void Key_folds_case_and_ignores_surrounding_spaces()
    {
        Assert.Equal(UsernameRules.Key("Ali.K"), UsernameRules.Key("ali.k"));
        Assert.Equal("admin", UsernameRules.Key("  admin "));
    }

    [Fact]
    public void Arabic_yeh_and_alef_maksura_match_persian_yeh()
    {
        Assert.Equal(UsernameRules.Key("علي"), UsernameRules.Key("علی"));
        Assert.Equal(UsernameRules.Key("ى"), UsernameRules.Key("ی"));
    }

    [Fact]
    public void Arabic_kaf_matches_persian_kaf()
    {
        Assert.Equal(UsernameRules.Key("كريم"), UsernameRules.Key("کریم"));
    }

    [Theory]
    [InlineData("Ali.K", "ali.k")]
    [InlineData("  Mohammad_ب ", "mohammad_ب")]
    [InlineData("يحيى", "یحیی")]
    [InlineData("علي.ك", "علی.ک")]
    [InlineData("Admin-01", "admin-01")]
    public void Key_matches_the_documented_sql_rule(string raw, string expected)
    {
        // lower(translate(btrim(raw), 'يىك', 'ییک')), hand-computed for each sample.
        Assert.Equal(expected, UsernameRules.Key(raw));
    }

    [Fact]
    public void Length_two_and_sixty_five_are_refused_with_the_persian_length_message()
    {
        Assert.False(UsernameRules.TryValidate("ab", out _, out _, out var shortError));
        Assert.Equal("نام کاربری باید بین ۳ تا ۶۴ نویسه باشد.", shortError);

        var tooLong = new string('a', 65);
        Assert.False(UsernameRules.TryValidate(tooLong, out _, out _, out var longError));
        Assert.Equal("نام کاربری باید بین ۳ تا ۶۴ نویسه باشد.", longError);
    }

    [Fact]
    public void Length_three_and_sixty_four_are_accepted()
    {
        Assert.True(UsernameRules.TryValidate("abc", out _, out _, out _));
        Assert.True(UsernameRules.TryValidate(new string('a', 64), out _, out _, out _));
    }

    [Theory]
    [InlineData("ali reza")]
    [InlineData("a@b")]
    [InlineData("ali‌reza")]
    [InlineData("ali/reza")]
    public void Spaces_at_signs_zwnj_and_symbols_are_refused_with_the_persian_character_message(string raw)
    {
        Assert.False(UsernameRules.TryValidate(raw, out _, out _, out var error));
        Assert.Equal("نام کاربری فقط می‌تواند شامل حروف، رقم، نقطه، خط تیره و زیرخط باشد.", error);
    }

    [Theory]
    [InlineData("a.b-c_1")]
    [InlineData("علی.کریمی")]
    [InlineData("کاربر_۱۲۳")]
    public void Latin_persian_and_separator_usernames_are_accepted(string raw)
    {
        Assert.True(UsernameRules.TryValidate(raw, out var trimmed, out var key, out var error));
        Assert.Null(error);
        Assert.Equal(raw, trimmed);
        Assert.Equal(UsernameRules.Key(raw), key);
    }

    [Fact]
    public void Null_input_is_refused_as_too_short()
    {
        Assert.False(UsernameRules.TryValidate(null, out _, out _, out var error));
        Assert.NotNull(error);
    }
}

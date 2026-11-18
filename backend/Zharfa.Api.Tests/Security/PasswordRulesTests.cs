namespace Zharfa.Api.Tests.Security;

using Zharfa.Api.Security;

/// <summary>T031 (research R20 item 3): the 8-character rule for application-set passwords. Pure.</summary>
public class PasswordRulesTests
{
    [Fact]
    public void Minimum_length_is_eight()
    {
        Assert.Equal(8, PasswordRules.MinLength);
    }

    [Fact]
    public void Seven_characters_are_refused_with_the_persian_message()
    {
        Assert.False(PasswordRules.TryValidate("1234567", out var error));
        Assert.Equal("گذرواژه باید حداقل ۸ نویسه باشد.", error);
    }

    [Fact]
    public void Eight_characters_are_accepted()
    {
        Assert.True(PasswordRules.TryValidate("12345678", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("        ")]
    public void Null_empty_and_whitespace_only_are_refused(string? password)
    {
        Assert.False(PasswordRules.TryValidate(password, out var error));
        Assert.NotNull(error);
    }
}

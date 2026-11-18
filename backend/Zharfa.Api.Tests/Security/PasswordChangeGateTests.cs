namespace Zharfa.Api.Tests.Security;

using Microsoft.AspNetCore.Http;
using Zharfa.Api.Security;

/// <summary>T035 (research R6): while a password change is required only four request shapes pass. Pure.</summary>
public class PasswordChangeGateTests
{
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/api/auth/change-password")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("GET", "/")]
    [InlineData("GET", "/assets/index.js")]
    [InlineData("GET", "/index.html")]
    public void Allowed_requests_pass(string method, string path)
    {
        Assert.True(PasswordChangeGate.IsAllowed(method, new PathString(path)));
    }

    [Theory]
    [InlineData("GET", "/api/overview/purchasing")]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/auth/me")]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("GET", "/api/vendor-financials/summary")]
    public void Other_api_requests_are_refused(string method, string path)
    {
        Assert.False(PasswordChangeGate.IsAllowed(method, new PathString(path)));
    }

    [Theory]
    [InlineData("get", "/API/AUTH/ME")]
    [InlineData("Post", "/Api/Auth/Change-Password")]
    public void Path_and_method_matching_is_case_insensitive(string method, string path)
    {
        Assert.True(PasswordChangeGate.IsAllowed(method, new PathString(path)));
    }

    [Fact]
    public void The_refusal_code_and_message_are_the_contract_values()
    {
        Assert.Equal("password-change-required", PasswordChangeGate.Code);
        Assert.Equal("ابتدا باید گذرواژهٔ خود را تغییر دهید.", PasswordChangeGate.Message);
    }
}

namespace Zharfa.Api.Tests.Security;

using Zharfa.Api.Security;

/// <summary>T032 (FR-008, research R4): the last active Admin cannot be demoted or deactivated. Pure.</summary>
public class LastAdminGuardTests
{
    [Fact]
    public void Demoting_the_only_active_admin_is_refused()
    {
        Assert.False(LastAdminGuard.Allows("admin", true, "finance", false, 0));
    }

    [Fact]
    public void Deactivating_the_only_active_admin_is_refused()
    {
        Assert.False(LastAdminGuard.Allows("admin", true, "admin", true, 0));
    }

    [Fact]
    public void With_another_active_admin_both_changes_are_allowed()
    {
        Assert.True(LastAdminGuard.Allows("admin", true, "finance", false, 1));
        Assert.True(LastAdminGuard.Allows("admin", true, "admin", true, 1));
    }

    [Fact]
    public void Keeping_the_admin_role_is_allowed()
    {
        Assert.True(LastAdminGuard.Allows("admin", true, "admin", false, 0));
    }

    [Theory]
    [InlineData("purchasing", "finance", false)]
    [InlineData("management", "admin", false)]
    [InlineData("finance", "purchasing", true)]
    public void Non_admin_targets_are_always_allowed(string currentRole, string? newRole, bool deactivating)
    {
        Assert.True(LastAdminGuard.Allows(currentRole, true, newRole, deactivating, 0));
    }

    [Fact]
    public void An_inactive_admin_target_is_always_allowed()
    {
        Assert.True(LastAdminGuard.Allows("admin", false, "finance", false, 0));
        Assert.True(LastAdminGuard.Allows("admin", false, "admin", true, 0));
    }

    [Fact]
    public void The_code_and_message_are_the_contract_values()
    {
        Assert.Equal("last-admin", LastAdminGuard.Code);
        Assert.Equal("دست‌کم یک مدیر سامانهٔ فعال باید باقی بماند.", LastAdminGuard.Message);
    }
}

namespace Zharfa.Api.Tests.Security;

using System;
using Zharfa.Api.Security;

/// <summary>
/// T033 (FR-033, research R5, data-model §1.3): one test per row of the sign-in table, with a fixed clock. Pure.
/// </summary>
public class SignInPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Wrong_password_below_the_limit_is_bad_credentials_and_counts_the_failure(int failed)
    {
        var decision = SignInPolicy.Decide(new SignInState(failed, null), passwordValid: false, Now);

        Assert.Equal(SignInOutcome.BadCredentials, decision.Outcome);
        Assert.Equal(failed + 1, decision.NewFailed);
        Assert.Null(decision.NewLockedUntil);
        Assert.False(decision.JustLocked);
    }

    [Fact]
    public void Fifth_wrong_password_locks_for_fifteen_minutes()
    {
        var decision = SignInPolicy.Decide(new SignInState(4, null), passwordValid: false, Now);

        Assert.Equal(SignInOutcome.Locked, decision.Outcome);
        Assert.Equal(0, decision.NewFailed);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), decision.NewLockedUntil);
        Assert.True(decision.JustLocked);
    }

    [Fact]
    public void A_correct_password_one_second_before_the_lock_ends_is_still_locked_and_unchanged()
    {
        var lockedUntil = Now.AddSeconds(1);
        var decision = SignInPolicy.Decide(new SignInState(5, lockedUntil), passwordValid: true, Now);

        Assert.Equal(SignInOutcome.Locked, decision.Outcome);
        Assert.Equal(5, decision.NewFailed);
        Assert.Equal(lockedUntil, decision.NewLockedUntil);
        Assert.False(decision.JustLocked);
    }

    [Fact]
    public void A_correct_password_exactly_at_the_lock_time_is_an_expired_lock_and_succeeds()
    {
        var decision = SignInPolicy.Decide(new SignInState(5, Now), passwordValid: true, Now);

        Assert.Equal(SignInOutcome.Success, decision.Outcome);
        Assert.Equal(0, decision.NewFailed);
        Assert.Null(decision.NewLockedUntil);
        Assert.False(decision.JustLocked);
    }

    [Fact]
    public void A_wrong_password_after_an_expired_lock_restarts_the_counter_at_one()
    {
        var decision = SignInPolicy.Decide(new SignInState(5, Now.AddMinutes(-1)), passwordValid: false, Now);

        Assert.Equal(SignInOutcome.BadCredentials, decision.Outcome);
        Assert.Equal(1, decision.NewFailed);
        Assert.Null(decision.NewLockedUntil);
    }

    [Fact]
    public void A_correct_password_below_the_limit_succeeds_and_resets_the_counter()
    {
        var decision = SignInPolicy.Decide(new SignInState(3, null), passwordValid: true, Now);

        Assert.Equal(SignInOutcome.Success, decision.Outcome);
        Assert.Equal(0, decision.NewFailed);
        Assert.Null(decision.NewLockedUntil);
    }

    [Fact]
    public void A_lock_in_the_future_refuses_any_password()
    {
        var lockedUntil = Now.AddMinutes(10);

        Assert.Equal(SignInOutcome.Locked, SignInPolicy.Decide(new SignInState(0, lockedUntil), passwordValid: false, Now).Outcome);
        Assert.Equal(SignInOutcome.Locked, SignInPolicy.Decide(new SignInState(0, lockedUntil), passwordValid: true, Now).Outcome);
    }

    [Fact]
    public void Constants_are_five_failures_and_fifteen_minutes()
    {
        Assert.Equal(5, SignInPolicy.MaxFailures);
        Assert.Equal(TimeSpan.FromMinutes(15), SignInPolicy.LockoutDuration);
    }
}

namespace Zharfa.Api.Security;

using System;

/// <summary>The sign-in counters stored on <c>app_user</c> (<c>failed_signins</c>, <c>locked_until</c>).</summary>
public sealed record SignInState(int Failed, DateTime? LockedUntilUtc);

public enum SignInOutcome
{
    Success,
    BadCredentials,
    Locked,
}

/// <summary>What the sign-in attempt does: the outcome, the counters to store and whether this attempt locked the account.</summary>
public sealed record SignInDecision(SignInOutcome Outcome, int NewFailed, DateTime? NewLockedUntil, bool JustLocked);

/// <summary>
/// Sign-in protection (FR-033, research R5, data-model §1.3). Five consecutive wrong passwords lock the account for
/// 15 minutes; a lock is checked before the password, so a locked account answers <see cref="SignInOutcome.Locked"/>
/// whatever is typed. Pure: the caller supplies the clock.
/// </summary>
public static class SignInPolicy
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static SignInDecision Decide(SignInState state, bool passwordValid, DateTime nowUtc)
    {
        // A lock still in the future refuses the attempt and leaves the state untouched.
        if (state.LockedUntilUtc is { } lockedUntil && lockedUntil > nowUtc)
        {
            return new SignInDecision(SignInOutcome.Locked, state.Failed, state.LockedUntilUtc, false);
        }

        // An expired lock restarts the counter before this attempt is applied.
        var failedBefore = state.LockedUntilUtc is not null ? 0 : state.Failed;

        if (passwordValid)
        {
            return new SignInDecision(SignInOutcome.Success, 0, null, false);
        }

        var failedNow = failedBefore + 1;
        if (failedNow >= MaxFailures)
        {
            return new SignInDecision(SignInOutcome.Locked, 0, nowUtc + LockoutDuration, true);
        }

        return new SignInDecision(SignInOutcome.BadCredentials, failedNow, null, false);
    }
}

namespace Zharfa.Api.Tests.Ai;

using Xunit;
using Zharfa.Api.Ai;

/// <summary>
/// Tests for <see cref="AiQuestionGate"/>.
/// ponytail: in-process ConcurrentDictionary only; single API replica on LAN compose;
/// upgrade to a PostgreSQL advisory lock if the API is scaled.
/// </summary>
public class AiQuestionGateTests
{
    [Fact]
    public void TryEnter_returns_true_for_first_call_and_false_for_concurrent_call_same_user()
    {
        var gate = new AiQuestionGate();
        const long userId = 42L;

        Assert.True(gate.TryEnter(userId));
        Assert.False(gate.TryEnter(userId));
    }

    [Fact]
    public void Release_allows_subsequent_enter_for_same_user()
    {
        var gate = new AiQuestionGate();
        const long userId = 42L;

        Assert.True(gate.TryEnter(userId));
        Assert.False(gate.TryEnter(userId));

        gate.Release(userId);

        Assert.True(gate.TryEnter(userId));
        gate.Release(userId);
    }

    [Fact]
    public void Different_users_can_both_enter_simultaneously()
    {
        var gate = new AiQuestionGate();
        const long user1 = 101L;
        const long user2 = 102L;

        Assert.True(gate.TryEnter(user1));
        Assert.True(gate.TryEnter(user2));

        Assert.False(gate.TryEnter(user1));
        Assert.False(gate.TryEnter(user2));

        gate.Release(user1);
        gate.Release(user2);
    }
}

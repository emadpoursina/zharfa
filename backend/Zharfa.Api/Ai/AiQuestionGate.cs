namespace Zharfa.Api.Ai;

using System.Collections.Concurrent;

/// <summary>
/// Gate enforcing at most one in-flight purchasing or demo chat request per user_id.
/// </summary>
public class AiQuestionGate
{
    // ponytail: in-process only; one API replica on LAN compose; upgrade to a PostgreSQL advisory lock if the API is scaled.
    private readonly ConcurrentDictionary<long, byte> _activeUsers = new();

    /// <summary>
    /// Attempts to mark the user as actively querying the AI.
    /// Returns true if entry was granted, false if a request for this user is already in flight.
    /// </summary>
    public bool TryEnter(long userId) => _activeUsers.TryAdd(userId, 0);

    /// <summary>
    /// Releases the in-flight lock for the given user.
    /// </summary>
    public void Release(long userId) => _activeUsers.TryRemove(userId, out _);
}

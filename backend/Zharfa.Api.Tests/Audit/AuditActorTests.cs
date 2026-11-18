namespace Zharfa.Api.Tests.Audit;

using System.Security.Claims;
using Zharfa.Api.Data;

/// <summary>
/// T011 (data-model §2.1): actor resolution. The override wins; otherwise the signed-in user; otherwise anonymous.
/// The user id follows the same order. Pure: builds principals and calls the static resolver.
/// </summary>
public class AuditActorTests
{
    [Fact]
    public void ActorOverride_wins_over_the_signed_in_user_and_is_cut_to_64_characters()
    {
        var entry = new AuditEntry("auth.signin.failed", ActorOverride: new string('a', 100));

        var (actor, _) = AuditLogger.ResolveActor(entry, Principal("carol", 7));

        Assert.Equal(64, actor.Length);
        Assert.StartsWith("aaaa", actor);
    }

    [Fact]
    public void ActorOverride_is_scrubbed()
    {
        var entry = new AuditEntry("auth.signin.failed", ActorOverride: "password=abc");

        var (actor, _) = AuditLogger.ResolveActor(entry, null);

        Assert.DoesNotContain("abc", actor);
    }

    [Fact]
    public void A_signed_in_user_gives_its_name_and_parsed_user_id()
    {
        var (actor, actorUserId) = AuditLogger.ResolveActor(new AuditEntry("report.opened"), Principal("purchasing", 7));

        Assert.Equal("purchasing", actor);
        Assert.Equal(7L, actorUserId);
    }

    [Fact]
    public void An_unauthenticated_principal_is_anonymous_with_no_user_id()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var (actor, actorUserId) = AuditLogger.ResolveActor(new AuditEntry("access.denied"), anonymous);

        Assert.Equal("anonymous", actor);
        Assert.Null(actorUserId);
    }

    [Fact]
    public void A_missing_user_is_anonymous_with_no_user_id()
    {
        var (actor, actorUserId) = AuditLogger.ResolveActor(new AuditEntry("access.denied"), null);

        Assert.Equal("anonymous", actor);
        Assert.Null(actorUserId);
    }

    [Fact]
    public void ActorUserIdOverride_wins_over_the_claim()
    {
        var entry = new AuditEntry("user.created", ActorUserIdOverride: 42);

        var (_, actorUserId) = AuditLogger.ResolveActor(entry, Principal("purchasing", 7));

        Assert.Equal(42L, actorUserId);
    }

    private static ClaimsPrincipal Principal(string name, long id) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, id.ToString()) },
        authenticationType: "test"));
}

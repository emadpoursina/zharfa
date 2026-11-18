namespace Zharfa.Api.Tests.Security;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Zharfa.Api.Data;
using Zharfa.Api.Security;

/// <summary>
/// T009 (spec 005 FR-018, contracts/api.md status table): a 403 writes one <c>access.denied</c> entry and the Persian
/// body; a 401 writes nothing; a failing audit write never changes the 403; a success calls the next middleware.
/// Pure: a <see cref="DefaultHttpContext"/> with a fake audit logger, no database and no network.
/// </summary>
public class AuditingAuthorizationResultHandlerTests
{
    private const string ForbiddenMessage = "شما دسترسی لازم برای انجام این عملیات را ندارید.";
    private const string UnauthenticatedMessage = "احراز هویت انجام نشده است یا نشست منقضی شده است.";

    [Fact]
    public async Task Forbidden_answers_403_with_the_persian_body_and_writes_one_access_denied_entry()
    {
        var (http, body, audit) = Build(Principal("purchasing"));
        var nextCalls = 0;

        await Handle(http, PolicyAuthorizationResult.Forbid(
            AuthorizationFailure.Failed(new[] { new PermissionRequirement("vendor.financials") })), () => nextCalls++);

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        var json = ReadBody(body);
        Assert.Equal(ForbiddenMessage, json.GetProperty("message").GetString());
        Assert.Equal("forbidden", json.GetProperty("code").GetString());

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("access.denied", entry.Action);
        Assert.Equal("GET api/vendor-financials/summary", entry.Target);
        Assert.Equal("permission=vendor.financials", entry.Detail);

        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task Challenged_answers_401_and_writes_no_audit_entry()
    {
        var (http, body, audit) = Build(new ClaimsPrincipal(new ClaimsIdentity()));
        var nextCalls = 0;

        await Handle(http, PolicyAuthorizationResult.Challenge(), () => nextCalls++);

        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal(UnauthenticatedMessage, ReadBody(body).GetProperty("message").GetString());
        Assert.Empty(audit.Entries);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task A_failing_audit_write_does_not_change_the_403()
    {
        var (http, body, audit) = Build(Principal("purchasing"), throwOnAudit: true);
        var nextCalls = 0;

        await Handle(http, PolicyAuthorizationResult.Forbid(
            AuthorizationFailure.Failed(new[] { new PermissionRequirement("admin") })), () => nextCalls++);

        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        Assert.Equal(ForbiddenMessage, ReadBody(body).GetProperty("message").GetString());
        Assert.Empty(audit.Entries);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task Success_calls_the_next_middleware_and_writes_nothing()
    {
        var (http, body, audit) = Build(Principal("admin"));
        var nextCalls = 0;

        await Handle(http, PolicyAuthorizationResult.Success(), () => nextCalls++);

        Assert.Equal(1, nextCalls);
        Assert.Empty(audit.Entries);
        Assert.Equal(0L, body.Length);
    }

    private static ClaimsPrincipal Principal(string role) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role) },
        authenticationType: "test"));

    private static (DefaultHttpContext Http, MemoryStream Body, FakeAuditLogger Audit) Build(
        ClaimsPrincipal user,
        bool throwOnAudit = false)
    {
        var audit = new FakeAuditLogger { Throw = throwOnAudit };
        var services = new ServiceCollection();
        services.AddSingleton<IAuditLogger>(audit);

        var body = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Request.Method = "GET";
        http.Response.Body = body;
        http.User = user;
        http.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/vendor-financials/summary"),
            0,
            EndpointMetadataCollection.Empty,
            "t"));

        return (http, body, audit);
    }

    private static Task Handle(HttpContext http, PolicyAuthorizationResult result, Action onNext)
    {
        RequestDelegate next = _ =>
        {
            onNext();
            return Task.CompletedTask;
        };

        var policy = new AuthorizationPolicy(
            new IAuthorizationRequirement[] { new PermissionRequirement("vendor.financials") },
            Array.Empty<string>());

        return new AuditingAuthorizationResultHandler().HandleAsync(next, http, policy, result);
    }

    private static JsonElement ReadBody(MemoryStream body)
    {
        body.Position = 0;
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        return document.RootElement.Clone();
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();

        public bool Throw { get; init; }

        public Task RecordAsync(string action, string? detail, CancellationToken ct = default) =>
            RecordAsync(new AuditEntry(action, Detail: detail), ct);

        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("audit store unavailable");
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}

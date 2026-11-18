namespace Zharfa.Api.Security;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;

/// <summary>
/// The shared refusal responses (contracts/api.md, status table). A 403 writes <c>access.denied</c> (FR-018);
/// a 401 writes nothing. The response is written first, so a failing audit write never changes the status.
/// </summary>
public static class AccessDenied
{
    public const string Message = "شما دسترسی لازم برای انجام این عملیات را ندارید.";
    public const string UnauthenticatedMessage = "احراز هویت انجام نشده است یا نشست منقضی شده است.";

    /// <summary>Audit target of a refused request: <c>METHOD route-pattern</c>, or the request path when no route matched.</summary>
    public static string TargetOf(HttpContext context)
    {
        var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = context.Request.Path.Value ?? string.Empty;
        }

        return $"{context.Request.Method} {pattern}";
    }

    /// <summary>403 <c>{ message, code = "forbidden" }</c>, then one <c>access.denied</c> entry naming the missing permission.</summary>
    public static async Task RespondAsync(HttpContext context, string permission)
    {
        await WriteAsync(context, StatusCodes.Status403Forbidden, new { message = Message, code = "forbidden" });

        try
        {
            var auditLogger = context.RequestServices.GetRequiredService<IAuditLogger>();
            await auditLogger.RecordAsync(
                new AuditEntry(AuditActions.AccessDenied, TargetOf(context), AuditDetail.Of(("permission", permission))),
                context.RequestAborted);
        }
        catch (Exception ex)
        {
            var logger = context.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("Zharfa.Api.Security.AccessDenied");
            logger?.LogWarning("Failed to record access.denied [{ExceptionType}]", ex.GetType().Name);
        }
    }

    /// <summary>401 <c>{ message }</c>. No audit entry: an anonymous caller is not an access decision.</summary>
    public static Task RespondUnauthenticatedAsync(HttpContext context) =>
        WriteAsync(context, StatusCodes.Status401Unauthorized, new { message = UnauthenticatedMessage });

    private static Task WriteAsync(HttpContext context, int statusCode, object body)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}

namespace Zharfa.Api.Audit;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Data;
using Zharfa.Api.Security;

/// <summary>
/// Writes <c>report.opened</c> (or <c>report.printed</c> with <see cref="Print"/>) for a report endpoint after a 2xx
/// result (research R9, data-model §2.3). The decision is <see cref="ReportOpenTracker.ShouldRecord"/>; the
/// response body is never read. A failed audit write never changes the response. Lookups carry no attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AuditReportOpenAttribute(string reportKey) : Attribute, IAsyncActionFilter
{
    private const string DocumentIdRoute = "documentId";

    /// <summary>The report key as written, or a route placeholder such as <c>{reportId}</c>.</summary>
    public string ReportKey { get; } = reportKey;

    /// <summary>True for the print twin of a report: always recorded, as <c>report.printed</c>.</summary>
    public bool Print { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // The route values are read before the action runs, so the handler cannot change them.
        var routeValues = context.RouteData.Values;
        var executed = await next();

        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            return;
        }

        try
        {
            await RecordAsync(executed, routeValues);
        }
        catch (Exception ex)
        {
            var logger = context.HttpContext.RequestServices.GetService<ILoggerFactory>()?
                .CreateLogger("Zharfa.Api.Audit.AuditReportOpen");
            logger?.LogWarning("Failed to record a report open [{ExceptionType}].", ex.GetType().Name);
        }
    }

    private async Task RecordAsync(ActionExecutedContext executed, RouteValueDictionary routeValues)
    {
        var httpContext = executed.HttpContext;
        var status = StatusOf(executed.Result) ?? httpContext.Response.StatusCode;
        if (!IsSuccess(status))
        {
            return;
        }

        var userId = httpContext.User.UserId();
        if (userId is null)
        {
            return;
        }

        var key = ResolveKey(ReportKey, routeValues);
        if (key is null)
        {
            return;
        }

        var query = httpContext.Request.Query;
        var scope = new Dictionary<string, string?>(AuditDetail.ReportSignatureParams(query), StringComparer.Ordinal);
        var viewParams = AuditDetail.ReportViewParams(query);

        var documentId = DocumentIdOf(routeValues);
        if (documentId is not null)
        {
            scope[DocumentIdRoute] = documentId;
        }

        var services = httpContext.RequestServices;
        var tracker = services.GetRequiredService<ReportOpenTracker>();
        if (!tracker.ShouldRecord(userId.Value, key, scope, viewParams, Print))
        {
            return;
        }

        var extra = documentId is null
            ? null
            : new[] { (DocumentIdRoute, (string?)documentId) };
        var detail = AuditDetail.ForReport(query, extra);

        var action = Print ? AuditActions.ReportPrinted : AuditActions.ReportOpened;
        var audit = services.GetRequiredService<IAuditLogger>();
        await audit.RecordAsync(new AuditEntry(action, key, detail), httpContext.RequestAborted);
    }

    /// <summary>
    /// The report key of a request: the literal template, or the value of the route placeholder when the template is
    /// <c>{name}</c>. A missing or empty route value yields null, so nothing is audited.
    /// </summary>
    public static string? ResolveKey(string template, RouteValueDictionary values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        if (template.Length > 2 && template[0] == '{' && template[^1] == '}')
        {
            var name = template[1..^1];
            return values.TryGetValue(name, out var value) && value is string text && text.Length > 0
                ? text
                : null;
        }

        return template;
    }

    /// <summary>True only for 200–299.</summary>
    public static bool IsSuccess(int status) => status is >= 200 and <= 299;

    /// <summary>The status of an action result, or null when the result does not carry one.</summary>
    private static int? StatusOf(IActionResult? result) => result switch
    {
        IStatusCodeActionResult coded => coded.StatusCode,
        _ => null,
    };

    private static string? DocumentIdOf(RouteValueDictionary values) =>
        values.TryGetValue(DocumentIdRoute, out var value) && value is not null
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
}

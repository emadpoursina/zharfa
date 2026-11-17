namespace Zharfa.Api.Platform;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Pure decision for <see cref="DemoOnlyAttribute"/> (spec 003 FR-021, contracts/api.md §5).
/// </summary>
public static class DemoOnlyRules
{
    /// <summary>Persian refusal message of the contract (the same envelope as every API error).</summary>
    public const string RefusalMessage = "این بخش در این نسخه فعال نیست.";

    /// <summary>
    /// Demo 1 pages, Demo 1 endpoints and the AI agent exist only in demo installs. Production
    /// refuses them for every signed-in role. Anonymous callers never reach this decision: the
    /// authorization filters run first and answer 401.
    /// </summary>
    public static bool IsRefused(DataModeOptions mode) => mode.IsProduction;

    /// <summary>The 404 result with the Persian envelope; no handler or query runs after it.</summary>
    public static NotFoundObjectResult RefusalResult() =>
        new(new { message = RefusalMessage });
}

/// <summary>
/// Endpoint filter for demo-only modules (FR-021, FR-023, research R12). Applied to Demo 1 controllers and
/// to the AI controller. In production it answers 404 before the action runs, so no handler code or database
/// query executes. In demo it passes the request through unchanged.
/// </summary>
/// <remarks>
/// It is an action filter, so the framework's authorization filters (cookie authentication and roles) run
/// before it: anonymous → 401, a signed-in user without the role → 403, and only then the mode check.
/// The order is set before the API's invalid-model-state filter, so production answers 404 even for a
/// malformed body.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DemoOnlyAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    /// <summary>Runs before the framework's invalid-model-state filter (order -2000).</summary>
    public int Order => -3000;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var mode = context.HttpContext.RequestServices.GetRequiredService<DataModeOptions>();
        if (DemoOnlyRules.IsRefused(mode))
        {
            context.Result = DemoOnlyRules.RefusalResult();
            return;
        }

        await next();
    }
}

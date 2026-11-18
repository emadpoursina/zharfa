namespace Zharfa.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Zharfa.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Analytics;
using Zharfa.Api.Platform;

[ApiController]
[Route("api/sales")]
[Authorize(Policy = Permissions.Overview)]
[DemoOnly]
public class SalesController : ControllerBase
{
    private readonly ISalesAnalytics _salesAnalytics;

    public SalesController(ISalesAnalytics salesAnalytics)
    {
        _salesAnalytics = salesAnalytics;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] long? categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId.HasValue && categoryId.Value <= 0)
        {
            throw new ArgumentException("شناسه دسته‌بندی باید عددی مثبت باشد.", nameof(categoryId));
        }

        var range = PeriodFilters.Resolve(from, to);
        var result = await _salesAnalytics.GetOverviewAsync(range, categoryId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("lines")]
    public async Task<IActionResult> GetLines(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] long? categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId.HasValue && categoryId.Value <= 0)
        {
            throw new ArgumentException("شناسه دسته‌بندی باید عددی مثبت باشد.", nameof(categoryId));
        }

        var range = PeriodFilters.Resolve(from, to);
        var result = await _salesAnalytics.GetLinesAsync(range, categoryId, cancellationToken);
        return Ok(result);
    }
}

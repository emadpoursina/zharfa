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
[Route("api/inventory")]
[Authorize(Policy = Permissions.Overview)]
[DemoOnly]
public class InventoryController : ControllerBase
{
    private readonly IInventoryAnalytics _inventoryAnalytics;

    public InventoryController(IInventoryAnalytics inventoryAnalytics)
    {
        _inventoryAnalytics = inventoryAnalytics;
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] long? categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId.HasValue && categoryId.Value <= 0)
        {
            throw new ArgumentException("شناسه دسته‌بندی باید عددی مثبت باشد.", nameof(categoryId));
        }

        var result = await _inventoryAnalytics.GetAlertsAsync(categoryId, cancellationToken);
        return Ok(result);
    }
}

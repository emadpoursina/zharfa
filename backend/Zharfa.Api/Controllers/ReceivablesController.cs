namespace Zharfa.Api.Controllers;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Analytics;

[ApiController]
[Route("api/receivables")]
[Authorize(Roles = "manager,admin")]
public class ReceivablesController : ControllerBase
{
    private readonly IReceivablesAnalytics _receivablesAnalytics;

    public ReceivablesController(IReceivablesAnalytics receivablesAnalytics)
    {
        _receivablesAnalytics = receivablesAnalytics;
    }

    [HttpGet("")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        var range = PeriodFilters.Resolve(from, to);
        var result = await _receivablesAnalytics.GetSummaryAsync(range, cancellationToken);
        return Ok(result);
    }

    [HttpGet("rows")]
    public async Task<IActionResult> GetRows(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        var range = PeriodFilters.Resolve(from, to);
        var result = await _receivablesAnalytics.GetRowsAsync(range, cancellationToken);
        return Ok(result);
    }
}

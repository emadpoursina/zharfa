namespace Zharfa.Api.Controllers;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Purchasing;

/// <summary>
/// <c>GET /api/overview/purchasing?month=YYYY-MM</c> (contracts/api.md §3): the Manager and Admin
/// Overview of purchasing. The role check is enforced here on the server; the UI only shows the page.
/// </summary>
[ApiController]
[Route("api/overview")]
[Authorize(Roles = "manager,admin")]
public sealed class OverviewController(PurchasingOverviewService overview) : ControllerBase
{
    [HttpGet("purchasing")]
    public async Task<IActionResult> Purchasing([FromQuery] string? month, CancellationToken cancellationToken)
    {
        JalaliMonth selected;
        // Only an absent (or empty) month means "current month"; whitespace is not trimmed and answers 400.
        if (string.IsNullOrEmpty(month))
        {
            selected = overview.CurrentMonth();
        }
        else if (!JalaliMonth.TryParse(month, out selected))
        {
            return BadRequest(new
            {
                message = "ماه جلالی نامعتبر است؛ قالب مجاز YYYY-MM با ماه بین ۱ تا ۱۲ است (مثلاً ۱۴۰۵-۰۸).",
            });
        }

        var result = await overview.GetAsync(selected, cancellationToken);
        return Ok(result);
    }
}

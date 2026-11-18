namespace Zharfa.Api.Controllers;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Zharfa.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Analytics;
using Zharfa.Api.Platform;

[ApiController]
[Route("api/meta")]
[Authorize(Policy = Permissions.Overview)]
[DemoOnly]
public class MetaController : ControllerBase
{
    private readonly ISalesAnalytics _salesAnalytics;

    public MetaController(ISalesAnalytics salesAnalytics)
    {
        _salesAnalytics = salesAnalytics;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var result = await _salesAnalytics.GetCategoriesAsync(cancellationToken);
        return Ok(result);
    }
}

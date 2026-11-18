namespace Zharfa.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Platform;
using Zharfa.Api.Security;

/// <summary>
/// Reserved vendor financials (research R15, contracts/api.md §5, FR-005). The endpoints exist so the permission
/// boundary is real from Phase 3; the figures arrive in Phase 6. Management and Finance get the "data unavailable"
/// body; every other role is refused by the <c>vendor.financials</c> policy. No query, no figure, not demo-only.
/// </summary>
[ApiController]
[Route("api/vendor-financials")]
[Authorize(Policy = Permissions.VendorFinancials)]
public sealed class VendorFinancialsController(DataModeOptions dataMode) : ControllerBase
{
    public const string Reason = "not-available-until-phase-6";
    public const string Message = "داده در دسترس نیست.";

    [HttpGet("summary")]
    public IActionResult Summary() => Ok(Unavailable());

    [HttpGet("vendors/{vendorKey}")]
    public IActionResult Vendor(string vendorKey) => Ok(Unavailable());

    private object Unavailable() => new
    {
        available = false,
        reason = Reason,
        message = Message,
        dataLabel = dataMode.DataLabel,
    };
}

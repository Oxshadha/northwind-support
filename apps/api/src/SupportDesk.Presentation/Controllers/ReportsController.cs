using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Application.Features.Reports.Queries.GetSlaSummary;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>
    /// SLA standing per agent or per priority for the tickets created in [from, to) (UTC;
    /// default the last 30 days): totals, each SLA state, breach rate and mean resolution hours.
    /// </summary>
    [HttpGet("sla-summary")]
    public Task<SlaSummaryDto> SlaSummary(
        [FromQuery] SlaSummaryRequest request,
        [FromServices] GetSlaSummaryQueryHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(request, ct);
}

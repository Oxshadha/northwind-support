using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Application.Features.Reports.Queries.GetSlaSummary;

/// <summary>
/// The SLA summary report: per agent or per priority, how the tickets created in a period
/// stand against their SLA right now, and how long the resolved ones took.
/// </summary>
public sealed class GetSlaSummaryQueryHandler(IReportQueries reports, IClock clock)
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromDays(30);

    public async Task<SlaSummaryDto> HandleAsync(SlaSummaryRequest request, CancellationToken ct)
    {
        var now = clock.UtcNow;

        // Query-string dates carry no time zone; the API speaks UTC throughout.
        var to = request.To is { } requestedTo ? DateTime.SpecifyKind(requestedTo, DateTimeKind.Utc) : now;
        var from = request.From is { } requestedFrom ? DateTime.SpecifyKind(requestedFrom, DateTimeKind.Utc) : to - DefaultPeriod;
        var groupBy = Enum.Parse<SlaSummaryGrouping>(request.GroupBy!, ignoreCase: true);

        var groups = await reports.GetSlaSummaryAsync(from, to, groupBy, ct);

        return new SlaSummaryDto(from, to, groupBy, now, groups);
    }
}

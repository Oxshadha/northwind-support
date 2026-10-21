using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Application.Abstractions;

/// <summary>The reporting side: aggregates computed by the database in a single query.</summary>
public interface IReportQueries
{
    /// <summary>SLA standing of the tickets created in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), per group.</summary>
    Task<IReadOnlyList<SlaSummaryGroupDto>> GetSlaSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        SlaSummaryGrouping groupBy,
        CancellationToken ct);
}

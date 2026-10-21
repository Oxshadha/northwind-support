using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Reports;

/// <summary>Query string of GET /api/reports/sla-summary.</summary>
/// <param name="From">Tickets created at or after this moment (UTC). Default: 30 days before <paramref name="To"/>.</param>
/// <param name="To">Tickets created before this moment (UTC). Default: now.</param>
/// <param name="GroupBy">agent or priority.</param>
public sealed record SlaSummaryRequest(DateTime? From, DateTime? To, string? GroupBy);

public enum SlaSummaryGrouping
{
    Agent,
    Priority
}

/// <summary>Where one group's tickets stand against their SLA.</summary>
public sealed class SlaSummaryGroupDto
{
    /// <summary>The agent's name ("Unassigned" for tickets nobody owns) or the priority.</summary>
    public string Key { get; set; } = string.Empty;

    public int? AgentId { get; set; }

    public TicketPriority? Priority { get; set; }

    public int Total { get; set; }

    public int WithinSla { get; set; }

    public int AtRisk { get; set; }

    public int Breached { get; set; }

    public int Met { get; set; }

    /// <summary>Tickets with no due date; left out of the breach rate.</summary>
    public int NoSla { get; set; }

    /// <summary>
    /// Breached as a percentage of the tickets that have an SLA, to one decimal place; null
    /// when none of them do.
    /// </summary>
    public double? BreachRatePercent =>
        Total - NoSla == 0 ? null : Math.Round(100.0 * Breached / (Total - NoSla), 1, MidpointRounding.AwayFromZero);

    /// <summary>Mean hours from creation to resolution, over resolved tickets only; null when none are.</summary>
    public double? AverageResolutionHours { get; set; }
}

/// <summary>Response of GET /api/reports/sla-summary.</summary>
public sealed record SlaSummaryDto(
    DateTime FromUtc,
    DateTime ToUtc,
    SlaSummaryGrouping GroupBy,
    DateTime GeneratedAtUtc,
    IReadOnlyList<SlaSummaryGroupDto> Groups);

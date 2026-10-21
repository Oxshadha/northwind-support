using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>One row of a ticket's escalation history: before, after, who and why.</summary>
public sealed class TicketEscalationDto
{
    public int Id { get; set; }

    public TicketPriority FromPriority { get; set; }

    public TicketPriority ToPriority { get; set; }

    public AgentSummaryDto? FromAgent { get; set; }

    public AgentSummaryDto? ToAgent { get; set; }

    public DateTime? FromDueAtUtc { get; set; }

    public DateTime? ToDueAtUtc { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string EscalatedBy { get; set; } = string.Empty;

    public int? EscalatedByAgentId { get; set; }

    public DateTime EscalatedAtUtc { get; set; }
}

/// <summary>Response of POST /api/tickets/{id}/escalate: the ticket as it now is, and the record written.</summary>
public sealed record TicketEscalatedDto(TicketDetailDto Ticket, TicketEscalationDto Escalation);

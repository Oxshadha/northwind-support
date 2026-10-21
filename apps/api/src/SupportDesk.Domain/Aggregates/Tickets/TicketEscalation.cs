using SupportDesk.Domain.Common;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// The record of one escalation: what the ticket looked like before and after, who escalated
/// it and why. Part of the <see cref="Ticket"/> aggregate and never changed once written.
/// </summary>
public sealed class TicketEscalation : Entity
{
    public const int ReasonMinLength = 5;
    public const int ReasonMaxLength = 500;
    public const int EscalatedByMaxLength = 100;

    private TicketEscalation()
    {
        // For EF Core.
    }

    internal TicketEscalation(
        TicketPriority fromPriority,
        TicketPriority toPriority,
        int? fromAgentId,
        int? toAgentId,
        DateTime? fromDueAtUtc,
        DateTime? toDueAtUtc,
        string reason,
        string escalatedBy,
        int escalatedByAgentId,
        DateTime escalatedAtUtc)
    {
        FromPriority = fromPriority;
        ToPriority = toPriority;
        FromAgentId = fromAgentId;
        ToAgentId = toAgentId;
        FromDueAtUtc = fromDueAtUtc;
        ToDueAtUtc = toDueAtUtc;
        Reason = reason;
        EscalatedBy = escalatedBy;
        EscalatedByAgentId = escalatedByAgentId;
        EscalatedAtUtc = escalatedAtUtc;
    }

    public int TicketId { get; private set; }

    public TicketPriority FromPriority { get; private set; }

    public TicketPriority ToPriority { get; private set; }

    public int? FromAgentId { get; private set; }

    public int? ToAgentId { get; private set; }

    public DateTime? FromDueAtUtc { get; private set; }

    public DateTime? ToDueAtUtc { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    /// <summary>The escalating agent's name as it was at the time, for a readable history.</summary>
    public string EscalatedBy { get; private set; } = string.Empty;

    /// <summary>
    /// Who escalated, exactly: names change and are not unique. Null only for rows written
    /// before sign-in existed, when the actor was free text.
    /// </summary>
    public int? EscalatedByAgentId { get; private set; }

    public DateTime EscalatedAtUtc { get; private set; }
}

using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Common;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// A support request raised by a customer. Every change to a ticket goes through one of its
/// methods, so its rules cannot be bypassed.
/// </summary>
/// <remarks>
/// The customer, the category and the assigned agent are other aggregates and are referenced
/// by id only.
/// </remarks>
public sealed class Ticket : AggregateRoot
{
    private readonly List<TicketEscalation> _escalations = [];

    private Ticket()
    {
        // For EF Core.
    }

    /// <summary>Human-friendly identifier shown to customers, e.g. TCK-0042.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public int CategoryId { get; private set; }

    /// <summary>Null while the ticket is waiting for an owner.</summary>
    public int? AssignedAgentId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When the team has committed to responding by. Null when no SLA applies.</summary>
    public DateTime? DueAtUtc { get; private set; }

    /// <summary>
    /// The length of the response window that ends at <see cref="DueAtUtc"/>, in seconds. The
    /// commitment made when the clock started, so a later configuration change cannot move an
    /// existing ticket into or out of risk.
    /// </summary>
    public int? SlaWindowSeconds { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// The escalations recorded against this ticket. Only those added since the ticket was
    /// loaded are present; the full history is read on the query side.
    /// </summary>
    public IReadOnlyCollection<TicketEscalation> Escalations => _escalations;

    /// <summary>True while the ticket still needs work from an agent.</summary>
    public bool IsOpen => Status is not (TicketStatus.Resolved or TicketStatus.Closed);

    /// <summary>
    /// Raises a new ticket for a customer. It starts as <see cref="TicketStatus.New"/>, with no
    /// owner and no due date.
    /// </summary>
    public static Ticket Raise(
        string reference,
        string title,
        string description,
        int customerId,
        int categoryId,
        TicketPriority priority,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Ticket
        {
            Reference = reference,
            Title = title.Trim(),
            Description = description.Trim(),
            CustomerId = customerId,
            CategoryId = categoryId,
            Priority = priority,
            Status = TicketStatus.New,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Moves the ticket through its lifecycle, keeping the resolution timestamp in step.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is closed.</exception>
    public void ChangeStatus(TicketStatus status, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is closed and cannot be moved to {status}.");
        }

        ResolvedAtUtc = status switch
        {
            TicketStatus.Resolved => nowUtc,
            TicketStatus.Closed => ResolvedAtUtc ?? nowUtc,
            _ => null
        };

        Status = status;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Hands the ticket to an agent, or takes it back off them when <paramref name="agentId"/>
    /// is null. Whether the agent may take it is checked by the caller, which can see agents.
    /// </summary>
    public void AssignTo(int? agentId, DateTime nowUtc)
    {
        AssignedAgentId = agentId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Starts the response clock: the ticket is due one window after <paramref name="startUtc"/>.</summary>
    public void StartSla(DateTime startUtc, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        SlaWindowSeconds = (int)Math.Round(window.TotalSeconds);
        DueAtUtc = startUtc.AddSeconds(SlaWindowSeconds.Value);
    }

    /// <summary>Checks the ticket can be escalated, before the caller does any work for it.</summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is closed, resolved or already Critical.</exception>
    public void EnsureCanBeEscalated()
    {
        if (!IsOpen)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is {Status} and cannot be escalated.");
        }

        if (Priority == TicketPriority.Critical)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is already Critical and cannot be escalated further.");
        }
    }

    /// <summary>
    /// Raises the priority one level, restarts the response clock from now for the new
    /// priority, hands the ticket to <paramref name="agentId"/>, and records all of it.
    /// </summary>
    /// <param name="agentId">The owner chosen for the escalated ticket (the caller can see agents).</param>
    /// <exception cref="BusinessRuleViolationException">The ticket cannot be escalated.</exception>
    public TicketEscalation Escalate(
        string reason,
        string escalatedBy,
        SlaPolicy sla,
        CustomerTier customerTier,
        int? agentId,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(escalatedBy);
        ArgumentNullException.ThrowIfNull(sla);

        EnsureCanBeEscalated();

        var before = (Priority, AssignedAgentId, DueAtUtc);

        Priority += 1;
        StartSla(nowUtc, sla.WindowFor(Priority, customerTier));
        AssignTo(agentId, nowUtc);

        var escalation = new TicketEscalation(
            fromPriority: before.Priority,
            toPriority: Priority,
            fromAgentId: before.AssignedAgentId,
            toAgentId: AssignedAgentId,
            fromDueAtUtc: before.DueAtUtc,
            toDueAtUtc: DueAtUtc,
            reason: reason.Trim(),
            escalatedBy: escalatedBy.Trim(),
            escalatedAtUtc: nowUtc);

        _escalations.Add(escalation);

        return escalation;
    }
}

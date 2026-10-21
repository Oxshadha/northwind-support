using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Decides a new ticket's priority: what was asked for, unless the category says otherwise.
/// </summary>
public static class PriorityPolicy
{
    public const TicketPriority DefaultPriority = TicketPriority.Medium;

    public static PriorityDecision Decide(TicketPriority? requested, Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.ForcesCriticalPriority)
        {
            return new PriorityDecision(
                TicketPriority.Critical,
                requested is null or TicketPriority.Critical
                    ? $"{category.Name} tickets are always Critical."
                    : $"{category.Name} tickets are always Critical, so the requested {requested} was raised.");
        }

        return requested is { } priority
            ? new PriorityDecision(priority, $"{priority}, as requested.")
            : new PriorityDecision(DefaultPriority, $"No priority requested, so {DefaultPriority}.");
    }
}

/// <summary>A ticket's priority and the reason it was chosen.</summary>
public sealed record PriorityDecision(TicketPriority Priority, string Reason);

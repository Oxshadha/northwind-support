using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Aggregates.Agents;

/// <summary>
/// Chooses who owns a ticket. Shared by raising a ticket and escalating one, so both follow
/// exactly the same rules.
/// </summary>
/// <remarks>
/// Eligible: active, a specialist in the category when it requires one, and strictly below
/// their open-ticket limit. Among those, the fewest open tickets wins; ties go to the most
/// spare capacity, then the lowest id, so the same data always gives the same answer.
/// </remarks>
public static class AgentSelector
{
    /// <summary>Picks an owner for a ticket that has none yet.</summary>
    public static AssignmentDecision Select(IEnumerable<AgentWorkload> agents, Category category)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(category);

        var eligible = agents.Where(agent => IsEligible(agent, category)).ToList();

        var chosen = eligible
            .OrderBy(agent => agent.OpenTicketCount)
            .ThenByDescending(agent => agent.SpareCapacity)
            .ThenBy(agent => agent.AgentId)
            .FirstOrDefault();

        if (chosen is null)
        {
            return AssignmentDecision.Unassigned(category.RequiresSpecialist
                ? $"Unassigned: no active {category.Name} specialist is below their open-ticket limit."
                : "Unassigned: no active agent is below their open-ticket limit.");
        }

        var pool = category.RequiresSpecialist
            ? $"{eligible.Count} eligible {category.Name} specialist(s)"
            : $"{eligible.Count} eligible agent(s)";

        return AssignmentDecision.AssignedTo(
            chosen.AgentId,
            $"Assigned to {chosen.FullName}: fewest open tickets " +
            $"({chosen.OpenTicketCount}/{chosen.MaxOpenTickets}) among {pool}.");
    }

    /// <summary>
    /// Re-checks the owner of a ticket that is already counted in its owner's workload, as
    /// on escalation: the owner keeps it while still eligible, otherwise it is reassigned.
    /// </summary>
    /// <remarks>
    /// The ticket is already part of its owner's open count, and keeping it adds no work, so
    /// it is left out of that count. Otherwise an agent at their limit would lose every
    /// ticket they escalate.
    /// </remarks>
    public static AssignmentDecision Reevaluate(int? currentAgentId, IEnumerable<AgentWorkload> agents, Category category)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(category);

        var workloads = agents.ToList();
        var current = workloads.FirstOrDefault(agent => agent.AgentId == currentAgentId);

        if (current is null)
        {
            return Select(workloads, category);
        }

        var withoutThisTicket = current with { OpenTicketCount = current.OpenTicketCount - 1 };

        if (IsEligible(withoutThisTicket, category))
        {
            return AssignmentDecision.AssignedTo(current.AgentId, $"Kept by {current.FullName}, who is still eligible.");
        }

        var others = workloads.Where(agent => agent.AgentId != current.AgentId);
        var replacement = Select(others, category);

        return replacement with { Reason = $"{current.FullName} {WhyIneligible(withoutThisTicket, category)}. {replacement.Reason}" };
    }

    public static bool IsEligible(AgentWorkload agent, Category category) =>
        agent.IsActive &&
        (!category.RequiresSpecialist || agent.SpecializationCategoryIds.Contains(category.Id)) &&
        agent.OpenTicketCount < agent.MaxOpenTickets;

    private static string WhyIneligible(AgentWorkload agent, Category category) =>
        !agent.IsActive ? "is no longer active"
        : category.RequiresSpecialist && !agent.SpecializationCategoryIds.Contains(category.Id)
            ? $"is not a {category.Name} specialist"
        : "is at their open-ticket limit";
}

/// <summary>Who owns a ticket (or nobody), and why.</summary>
public sealed record AssignmentDecision(int? AgentId, string Reason)
{
    public static AssignmentDecision AssignedTo(int agentId, string reason) => new(agentId, reason);

    public static AssignmentDecision Unassigned(string reason) => new(null, reason);
}

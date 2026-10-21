namespace SupportDesk.Domain.Aggregates.Agents;

/// <summary>
/// An agent as assignment sees them: who they are, what they may handle and how much open
/// work they already hold. A snapshot read for one decision, not an aggregate.
/// </summary>
/// <param name="OpenTicketCount">Tickets assigned to the agent that are not Resolved or Closed.</param>
public sealed record AgentWorkload(
    int AgentId,
    string FullName,
    bool IsActive,
    int MaxOpenTickets,
    int OpenTicketCount,
    IReadOnlyCollection<int> SpecializationCategoryIds)
{
    /// <summary>Room for further open tickets before the agent reaches their limit.</summary>
    public int SpareCapacity => MaxOpenTickets - OpenTicketCount;
}

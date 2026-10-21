using SupportDesk.Application.Contracts.Agents;

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// The read side of agents.
/// </summary>
public interface IAgentQueries
{
    Task<IReadOnlyList<AgentDto>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Whether this agent exists and is active: asked on every authenticated request, so a
    /// deactivated agent's still-valid token stops working at once.
    /// </summary>
    Task<bool> IsActiveAsync(int agentId, CancellationToken ct);
}

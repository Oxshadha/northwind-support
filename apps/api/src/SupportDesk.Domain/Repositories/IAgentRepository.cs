using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Loads <see cref="Agent"/> aggregates, and the workloads assignment decides on.
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>The agent with this sign-in email, tracked so a rehashed password can be saved.</summary>
    Task<Agent?> GetByEmailAsync(string email, CancellationToken ct);

    /// <summary>Every agent with their open-ticket count and specializations, in one read.</summary>
    Task<IReadOnlyList<AgentWorkload>> GetWorkloadsAsync(CancellationToken ct);
}

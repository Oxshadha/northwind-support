using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Queries.GetCurrentAgent;

/// <summary>
/// Returns the signed-in agent, read fresh from the database rather than from the token, so
/// an agent deactivated since signing in is told so here.
/// </summary>
public sealed class GetCurrentAgentQueryHandler(ICurrentUser currentUser, IAgentRepository agents)
{
    /// <exception cref="UnauthorizedException">The agent no longer exists or is inactive.</exception>
    public async Task<CurrentAgentDto> HandleAsync(CancellationToken ct)
    {
        var agent = await agents.GetByIdAsync(currentUser.AgentId, ct);

        if (agent is null || !agent.IsActive)
        {
            throw new UnauthorizedException("This account can no longer sign in.");
        }

        return new CurrentAgentDto(agent.Id, agent.FullName, agent.Email);
    }
}

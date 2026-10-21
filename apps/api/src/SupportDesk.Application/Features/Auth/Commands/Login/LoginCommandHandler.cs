using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Commands.Login;

/// <summary>
/// Signs an agent in with email and password and issues their access token.
/// </summary>
/// <remarks>
/// Every way of failing - unknown email, no credentials, wrong password, inactive agent -
/// gives the same error after the same amount of work (one password check), so neither the
/// message nor the response time tells a caller which emails belong to agents.
/// </remarks>
public sealed class LoginCommandHandler(
    IAgentRepository agents,
    IPasswordHasher passwords,
    ITokenService tokens,
    IUnitOfWork unitOfWork,
    ILogger<LoginCommandHandler> logger)
{
    public const string FailureMessage = "Email or password is incorrect.";

    /// <exception cref="UnauthorizedException">The credentials do not sign anyone in.</exception>
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var agent = await agents.GetByEmailAsync(request.Email.Trim(), ct);

        // Exactly one password check on every path, including an unknown email.
        var check = passwords.Verify(agent?.PasswordHash ?? passwords.DummyHash, request.Password);

        if (agent?.PasswordHash is null || check == PasswordCheck.Failed || !agent.IsActive)
        {
            logger.LogInformation("Sign-in refused.");
            throw new UnauthorizedException(FailureMessage);
        }

        if (check == PasswordCheck.SucceededRehashNeeded)
        {
            // Hashed with older settings (e.g. fewer iterations): upgrade it while we have the password.
            agent.SetPasswordHash(passwords.Hash(request.Password));
            await unitOfWork.SaveChangesAsync(ct);
        }

        var token = tokens.Issue(agent.Id, agent.FullName, agent.Email);

        logger.LogInformation("Agent {AgentId} signed in.", agent.Id);

        return new LoginResponse(token.Token, token.ExpiresAtUtc, new CurrentAgentDto(agent.Id, agent.FullName, agent.Email));
    }
}

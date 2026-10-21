namespace SupportDesk.Application.Abstractions;

/// <summary>Issues the signed access token an agent presents on every API call.</summary>
public interface ITokenService
{
    IssuedToken Issue(int agentId, string fullName, string email);
}

/// <summary>A signed token and the moment it stops being accepted.</summary>
public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);

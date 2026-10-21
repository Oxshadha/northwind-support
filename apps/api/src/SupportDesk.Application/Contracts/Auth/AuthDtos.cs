namespace SupportDesk.Application.Contracts.Auth;

/// <summary>Body of POST /api/auth/login.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Enough about the signed-in agent to greet them.</summary>
public sealed record CurrentAgentDto(int Id, string FullName, string Email);

/// <summary>Response of POST /api/auth/login: the token to send as a bearer, and when it expires.</summary>
public sealed record LoginResponse(string Token, DateTime ExpiresAtUtc, CurrentAgentDto Agent);

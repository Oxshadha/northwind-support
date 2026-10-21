using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Infrastructure.Auth;

/// <summary>
/// Passwords hashed by ASP.NET Core Identity's PasswordHasher: PBKDF2 with HMAC-SHA512, a
/// random 128-bit salt per password, and the settings stored in the hash itself, so they can
/// be raised later without breaking existing hashes.
/// </summary>
/// <remarks>
/// Identity defaults to 100,000 iterations; OWASP's Password Storage Cheat Sheet asks for
/// 210,000 for PBKDF2-HMAC-SHA512. Hashes made with fewer are reported as needing a rehash,
/// and sign-in replaces them.
/// </remarks>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    public const int IterationCount = 210_000;

    // The hasher never reads the user; Identity's interface takes one for custom hashers.
    private static readonly Agent NoUser = null!;

    private readonly PasswordHasher<Agent> _hasher =
        new(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));

    public IdentityPasswordHasher()
    {
        DummyHash = _hasher.HashPassword(NoUser, Guid.NewGuid().ToString());
    }

    public string DummyHash { get; }

    public string Hash(string password) => _hasher.HashPassword(NoUser, password);

    public PasswordCheck Verify(string passwordHash, string password) =>
        _hasher.VerifyHashedPassword(NoUser, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Succeeded,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SucceededRehashNeeded,
            _ => PasswordCheck.Failed
        };
}

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Hashes and checks passwords with a vetted, salted, deliberately slow algorithm.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// A valid hash of no real password. Checking a password against it costs exactly what a
    /// real check costs, so a sign-in for an unknown email takes as long as a wrong password.
    /// </summary>
    string DummyHash { get; }

    string Hash(string password);

    PasswordCheck Verify(string passwordHash, string password);
}

public enum PasswordCheck
{
    Failed,
    Succeeded,

    /// <summary>Correct, but hashed with older settings; store a fresh hash.</summary>
    SucceededRehashNeeded
}

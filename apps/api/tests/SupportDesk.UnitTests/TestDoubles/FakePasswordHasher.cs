using SupportDesk.Application.Abstractions;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>
/// A transparent stand-in for the real (deliberately slow) hasher, for tests that only need
/// some hash; it records every check so tests can count them.
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    private const string Prefix = "fake-hash:";

    public string DummyHash => Prefix + "no-such-password";

    public List<string> CheckedHashes { get; } = [];

    /// <summary>Make the next check report that the hash needs upgrading.</summary>
    public bool ReportRehashNeeded { get; set; }

    public string Hash(string password) => Prefix + password;

    public PasswordCheck Verify(string passwordHash, string password)
    {
        CheckedHashes.Add(passwordHash);

        if (passwordHash != Prefix + password)
        {
            return PasswordCheck.Failed;
        }

        return ReportRehashNeeded ? PasswordCheck.SucceededRehashNeeded : PasswordCheck.Succeeded;
    }
}

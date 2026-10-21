using System.Buffers.Binary;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Auth;

namespace SupportDesk.UnitTests.Infrastructure.Auth;

/// <summary>The real hasher: salted, slow, and able to recognise hashes worth upgrading.</summary>
public class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Hash_NeverContainsThePassword_AndIsSaltedPerCall()
    {
        var first = _hasher.Hash("LocalDev-Only-2026!");
        var second = _hasher.Hash("LocalDev-Only-2026!");

        Assert.DoesNotContain("LocalDev", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verify_AcceptsTheRightPassword_AndRejectsAWrongOne()
    {
        var hash = _hasher.Hash("LocalDev-Only-2026!");

        Assert.Equal(PasswordCheck.Succeeded, _hasher.Verify(hash, "LocalDev-Only-2026!"));
        Assert.Equal(PasswordCheck.Failed, _hasher.Verify(hash, "localdev-only-2026!"));
    }

    [Fact]
    public void Hash_UsesPbkdf2WithHmacSha512_AtTheConfiguredIterations()
    {
        // Identity V3 format: version byte, then PRF, iteration count, salt length - big-endian.
        var bytes = Convert.FromBase64String(_hasher.Hash("anything"));

        Assert.Equal(0x01, bytes[0]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1))); // 2 = HMACSHA512
        Assert.Equal((uint)IdentityPasswordHasher.IterationCount, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5)));
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9)));
    }

    [Fact]
    public void Verify_AHashMadeWithFewerIterations_AsksForARehash()
    {
        var weaker = new PasswordHasher<Agent>(Options.Create(new PasswordHasherOptions { IterationCount = 100_000 }));
        var oldHash = weaker.HashPassword(null!, "LocalDev-Only-2026!");

        Assert.Equal(PasswordCheck.SucceededRehashNeeded, _hasher.Verify(oldHash, "LocalDev-Only-2026!"));
    }

    [Fact]
    public void DummyHash_IsARealHash_ThatNoGuessMatches() =>
        Assert.Equal(PasswordCheck.Failed, _hasher.Verify(_hasher.DummyHash, "LocalDev-Only-2026!"));
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Infrastructure.Auth;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Auth;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "northwind-support-api",
        Audience = "northwind-support-web",
        SigningKey = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
        LifetimeMinutes = 480
    };

    private readonly FixedClock _clock = new();

    private JwtTokenService Service(JwtOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options), _clock);

    private static TokenValidationParameters ValidationWith(string signingKey) => new()
    {
        ValidIssuer = Options.Issuer,
        ValidAudience = Options.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey)),
        // The fixed clock is in the past; check the signature and claims, not real-world expiry.
        ValidateLifetime = false
    };

    [Fact]
    public async Task Issue_NamesTheAgent_AndExpiresAfterTheConfiguredLifetime()
    {
        var issued = Service().Issue(4, "Sara Lindqvist", "sara.lindqvist@northwind-support.example");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, ValidationWith(Options.SigningKey));

        Assert.True(result.IsValid);
        Assert.Equal("4", result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal("Sara Lindqvist", result.Claims[JwtRegisteredClaimNames.Name]);
        Assert.Equal(_clock.UtcNow.AddMinutes(480), issued.ExpiresAtUtc);
        Assert.Equal(issued.ExpiresAtUtc, ((JsonWebToken)result.SecurityToken).ValidTo);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRejected()
    {
        var forged = Service(new JwtOptions
        {
            Issuer = Options.Issuer,
            Audience = Options.Audience,
            SigningKey = "an-attackers-own-key-also-thirty-two-bytes-or-more",
            LifetimeMinutes = 480
        }).Issue(1, "Alex Turner", "alex.turner@northwind-support.example");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(forged.Token, ValidationWith(Options.SigningKey));

        Assert.False(result.IsValid);
    }
}

public class JwtOptionsValidatorTests
{
    private static JwtOptions With(string key, int lifetime = 480) => new()
    {
        Issuer = "northwind-support-api",
        Audience = "northwind-support-web",
        SigningKey = key,
        LifetimeMinutes = lifetime
    };

    private static ValidateOptionsResult Validate(JwtOptions options, string environment) =>
        new JwtOptionsValidator(new FakeEnvironment(environment)).Validate(null, options);

    [Fact]
    public void AKeyShorterThan32Bytes_IsRefused() =>
        Assert.True(Validate(With("too-short"), Environments.Production).Failed);

    [Fact]
    public void TheDevelopmentKey_IsAccepted_InDevelopment() =>
        Assert.True(Validate(With(JwtOptions.DevelopmentKeyPrefix + new string('k', 40)), Environments.Development).Succeeded);

    [Fact]
    public void TheDevelopmentKey_IsRefused_AnywhereElse()
    {
        var result = Validate(With(JwtOptions.DevelopmentKeyPrefix + new string('k', 40)), Environments.Production);

        Assert.True(result.Failed);
        Assert.Contains("development key", result.FailureMessage);
    }

    [Fact]
    public void ARealKey_IsAccepted_InProduction() =>
        Assert.True(Validate(With(Convert.ToBase64String(new byte[48])), Environments.Production).Succeeded);

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "SupportDesk";

        public string ContentRootPath { get; set; } = "/";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

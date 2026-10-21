using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SupportDesk.Infrastructure.Auth;

/// <summary>
/// The <c>Jwt</c> configuration section. The signing key is a secret: it comes from the
/// environment (<c>Jwt__SigningKey</c>) or a developer's local configuration, never from code.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Keys starting with this are for local development only and refused anywhere else.</summary>
    public const string DevelopmentKeyPrefix = "dev-only-";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    /// <summary>HMAC-SHA256 key; at least 32 bytes, as the algorithm requires.</summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>How long a token is accepted. 480 minutes: one working shift, then sign in again.</summary>
    public int LifetimeMinutes { get; init; }

    public byte[] SigningKeyBytes() => Encoding.UTF8.GetBytes(SigningKey);
}

/// <summary>
/// Refuses to start without a usable key, and refuses the development key outside
/// Development, so a deployment cannot silently run on a published fixture.
/// </summary>
internal sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Issuer and Jwt:Audience are required.");
        }

        if (options.SigningKeyBytes().Length < 32)
        {
            failures.Add("Jwt:SigningKey must be at least 32 bytes. Set it with the Jwt__SigningKey environment variable.");
        }

        if (!environment.IsDevelopment() && options.SigningKey.StartsWith(JwtOptions.DevelopmentKeyPrefix, StringComparison.Ordinal))
        {
            failures.Add("Jwt:SigningKey is the development key; set a real one with Jwt__SigningKey.");
        }

        if (options.LifetimeMinutes is < 5 or > 1440)
        {
            failures.Add("Jwt:LifetimeMinutes must be between 5 and 1440.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

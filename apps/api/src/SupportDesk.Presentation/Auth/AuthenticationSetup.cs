using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Infrastructure.Auth;

namespace SupportDesk.Presentation.Auth;

public static class AuthenticationSetup
{
    /// <summary>
    /// Bearer-token authentication, and one rule for the whole API: every endpoint needs a
    /// signed-in agent unless it explicitly opts out with [AllowAnonymous]. A new endpoint is
    /// therefore closed by default; nobody has to remember to protect it.
    /// </summary>
    public static IServiceCollection AddAgentAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Validation settings come from the same validated options the tokens are issued with.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Keep claim names as issued ("sub", "name") instead of mapping them to long URIs.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.SigningKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = "name",
                    // The default allows five minutes past expiry; thirty seconds covers clock drift.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                bearer.Events = new JwtBearerEvents
                {
                    // A valid signature only proves who the agent was when they signed in. Check
                    // they may still use the API, so deactivation takes effect at once rather
                    // than when the token expires.
                    OnTokenValidated = async context =>
                    {
                        var agents = context.HttpContext.RequestServices.GetRequiredService<IAgentQueries>();
                        var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                        if (!int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var agentId) ||
                            !await agents.IsActiveAsync(agentId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The agent is no longer active.");
                        }
                    },

                    // Answer a missing, expired or revoked token with a problem document, like every other error.
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new ProblemDetails
                        {
                            Status = StatusCodes.Status401Unauthorized,
                            Title = "Unauthorized",
                            Detail = "Sign in to use the API."
                        });
                    }
                };
            });

        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }
}

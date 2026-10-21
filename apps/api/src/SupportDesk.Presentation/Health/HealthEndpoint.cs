using System.Net.Mime;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Presentation.Health;

public static class HealthEndpoint
{
    public const string Path = "/health";

    /// <summary>Whether the API can reach its database: a real query, not just "the process is up".</summary>
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddDbContextCheck<SupportDbContext>("database");
        return services;
    }

    /// <summary>
    /// GET /health: 200 when healthy, 503 when not. Open without a token, so a container
    /// orchestrator or load balancer can probe it; it names each check's status and nothing
    /// more (no connection strings, no exception details).
    /// </summary>
    public static IEndpointConventionBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapHealthChecks(Path, new HealthCheckOptions { ResponseWriter = WriteAsync }).AllowAnonymous();

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = MediaTypeNames.Application.Json;

        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString())
        });
    }
}

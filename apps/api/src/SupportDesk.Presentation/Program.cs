using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using SupportDesk.Application;
using SupportDesk.Infrastructure;
using SupportDesk.Presentation.Auth;
using SupportDesk.Presentation.Extensions;
using SupportDesk.Presentation.Filters;
using SupportDesk.Presentation.Health;
using SupportDesk.Presentation.Middleware;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "frontend";

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        // Enums travel as their names, both ways, so the API reads well from the browser.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddCors(options => options.AddPolicy(
    FrontendCorsPolicy,
    policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Swagger's "Authorize" button: paste a token from POST /api/auth/login to try the API.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "The token returned by POST /api/auth/login."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAgentAuthentication();
builder.Services.AddApiHealthChecks();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // Served by middleware ahead of routing, so the fallback policy does not apply: open.
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Keeps a developer machine (or a local container) one command away from a working database.
// Off unless configured: a real deployment migrates as a separate, deliberate step.
if (app.Configuration.GetValue<bool>("Database:MigrateAndSeedOnStartup"))
{
    await app.MigrateAndSeedAsync();
}

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// The one other endpoint open without a token, opted out here, next to the rest of the routing.
app.MapApiHealthChecks();

app.Run();

/// <summary>The entry point, visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program;

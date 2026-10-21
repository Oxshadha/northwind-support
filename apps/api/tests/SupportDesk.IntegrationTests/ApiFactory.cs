using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.IntegrationTests;

/// <summary>
/// The API as configured for Development (its JWT settings, Swagger), on an in-memory SQLite
/// database holding the seed data and the development credentials.
/// </summary>
/// <remarks>
/// Two substitutions, both because SQLite is not SQL Server: the database provider, and the
/// assignment lock (sp_getapplock), which is verified against SQL Server separately.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:MigrateAndSeedOnStartup", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SupportDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SupportDbContext>>();
            services.AddDbContext<SupportDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IAssignmentLock>();
            services.AddScoped<IAssignmentLock, PassThroughAssignmentLock>();
        });
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SupportDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<SupportDbSeeder>().SeedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    private sealed class PassThroughAssignmentLock : IAssignmentLock
    {
        public Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
            operation(ct);
    }
}

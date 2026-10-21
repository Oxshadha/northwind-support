using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>
/// The real EF Core model on an in-memory SQLite database, holding the development seed data
/// (40 tickets), for tests that must run real queries without a SQL Server.
/// </summary>
public sealed class SeededDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SeededDatabase(SqliteConnection connection, SupportDbContext db, FixedClock clock)
    {
        _connection = connection;
        Db = db;
        Clock = clock;
    }

    public SupportDbContext Db { get; }

    public FixedClock Clock { get; }

    public static async Task<SeededDatabase> CreateAsync()
    {
        // The in-memory database lives as long as this open connection.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var clock = new FixedClock();
        var db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await new SupportDbSeeder(db, clock, NullLogger<SupportDbSeeder>.Instance).SeedAsync();

        return new SeededDatabase(connection, db, clock);
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

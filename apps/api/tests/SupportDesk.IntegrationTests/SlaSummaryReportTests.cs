using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Auth;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Services;

namespace SupportDesk.IntegrationTests;

/// <summary>
/// The SLA summary on a real SQL Server: a throwaway database built by the real migrations
/// and seeded, the report compared with the domain rule ticket by ticket, and the number of
/// SQL statements it takes counted.
/// </summary>
public sealed class SlaSummaryReportTests : IAsyncLifetime
{
    private static readonly SlaPolicy Sla = new SupportDesk.Infrastructure.Configuration.SlaOptions
    {
        BaseWindowHours = new() { [TicketPriority.Low] = 72, [TicketPriority.Medium] = 24, [TicketPriority.High] = 8, [TicketPriority.Critical] = 4 },
        PremiumCustomerMultiplier = 0.5,
        MinimumWindowHours = 1,
        AtRiskThresholdPercent = 25
    }.ToPolicy();

    private readonly CommandCounter _commands = new();
    private SupportDbContext _db = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(SqlServerFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        _connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = $"SupportDesk_ReportTests_{Guid.NewGuid():N}" }.ConnectionString;
        _db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlServer(_connectionString)
            .AddInterceptors(_commands)
            .Options);

        await _db.Database.MigrateAsync();
        await new SupportDbSeeder(_db, new SystemClock(), new IdentityPasswordHasher(), NullLogger<SupportDbSeeder>.Instance).SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.Database.EnsureDeletedAsync();
            await _db.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task ByPriority_CountsEveryTicketInTheRightState_InOneStatement()
    {
        var clock = new SystemClock();
        var expected = await ExpectedAsync(clock.UtcNow, t => t.Priority.ToString());

        _commands.Reset();
        var groups = await new ReportQueries(_db, clock, Sla).GetSlaSummaryAsync(
            clock.UtcNow.AddDays(-30), clock.UtcNow.AddMinutes(1), SlaSummaryGrouping.Priority, CancellationToken.None);

        Assert.Equal(1, _commands.Count);
        AssertMatches(expected, groups);
    }

    [SqlServerFact]
    public async Task ByAgent_NamesEachAgent_AndGroupsUnassignedTickets_InOneStatement()
    {
        var clock = new SystemClock();
        var names = await _db.Set<Domain.Aggregates.Agents.Agent>().ToDictionaryAsync(a => a.Id, a => a.FullName);
        var expected = await ExpectedAsync(clock.UtcNow, t => t.AssignedAgentId is { } id ? names[id] : "Unassigned");

        _commands.Reset();
        var groups = await new ReportQueries(_db, clock, Sla).GetSlaSummaryAsync(
            clock.UtcNow.AddDays(-30), clock.UtcNow.AddMinutes(1), SlaSummaryGrouping.Agent, CancellationToken.None);

        Assert.Equal(1, _commands.Count);
        AssertMatches(expected, groups);
        Assert.Equal("Unassigned", groups[^1].Key);
    }

    [SqlServerFact]
    public async Task APeriodWithNoTickets_HasNoGroups()
    {
        var groups = await new ReportQueries(_db, new SystemClock(), Sla).GetSlaSummaryAsync(
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2000, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            SlaSummaryGrouping.Agent, CancellationToken.None);

        Assert.Empty(groups);
    }

    /// <summary>The same report worked out in C# from the tickets themselves, with the domain rule.</summary>
    private async Task<Dictionary<string, (int Total, int Within, int AtRisk, int Breached, int Met, double? Hours)>> ExpectedAsync(
        DateTime now, Func<Ticket, string> key)
    {
        var tickets = await _db.Set<Ticket>().AsNoTracking().ToListAsync();

        return tickets.GroupBy(key).ToDictionary(g => g.Key, g =>
        {
            var states = g.Select(t => SlaEvaluator.Evaluate(t.DueAtUtc, t.ResolvedAtUtc, t.SlaWindowSeconds, Sla.AtRiskThresholdPercent, now)).ToList();
            var resolved = g.Where(t => t.ResolvedAtUtc is not null).ToList();
            double? hours = resolved.Count == 0
                ? null
                : Math.Round(resolved.Average(t => (t.ResolvedAtUtc!.Value - t.CreatedAtUtc).TotalSeconds) / 3600, 1, MidpointRounding.AwayFromZero);

            return (g.Count(), states.Count(s => s == SlaStatus.WithinSla), states.Count(s => s == SlaStatus.AtRisk),
                states.Count(s => s == SlaStatus.Breached), states.Count(s => s == SlaStatus.Met), hours);
        });
    }

    private static void AssertMatches(
        Dictionary<string, (int Total, int Within, int AtRisk, int Breached, int Met, double? Hours)> expected,
        IReadOnlyList<SlaSummaryGroupDto> groups)
    {
        Assert.Equal(expected.Keys.Order(), groups.Select(g => g.Key).Order());

        foreach (var group in groups)
        {
            var want = expected[group.Key];
            Assert.Equal(want, (group.Total, group.WithinSla, group.AtRisk, group.Breached, group.Met, group.AverageResolutionHours));
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

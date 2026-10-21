using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

/// <summary>
/// The report classifies tickets with one SQL CASE built from the filter's predicates. Run in
/// the database, it must give every seeded ticket the same state as the domain rule.
/// </summary>
public sealed class SlaSqlTests : IAsyncLifetime
{
    private SeededDatabase _database = null!;

    public async Task InitializeAsync() => _database = await SeededDatabase.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task StatusOf_InSql_MatchesTheDomainRule_ForEveryTicket()
    {
        var now = _database.Clock.UtcNow;
        var sla = Policies.DefaultSla();

        var inSql = await _database.Db.Set<Ticket>()
            .Select(SlaSql.StatusOf(now, sla.AtRiskThresholdPercent))
            .ToListAsync();

        var tickets = await _database.Db.Set<Ticket>().AsNoTracking().ToListAsync();
        var inDomain = tickets.Select(t =>
            SlaEvaluator.Evaluate(t.DueAtUtc, t.ResolvedAtUtc, t.SlaWindowSeconds, sla.AtRiskThresholdPercent, now));

        Assert.Equal(40, inSql.Count);
        Assert.Equal(inDomain, inSql);
    }
}

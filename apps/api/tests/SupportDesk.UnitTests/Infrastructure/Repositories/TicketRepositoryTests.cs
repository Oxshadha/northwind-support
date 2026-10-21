using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Repositories;

public sealed class TicketRepositoryTests : IAsyncLifetime
{
    private SeededDatabase _database = null!;
    private TicketRepository _tickets = null!;

    public async Task InitializeAsync()
    {
        _database = await SeededDatabase.CreateAsync();
        _tickets = new TicketRepository(_database.Db);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task NextReference_FollowsTheLastReference() =>
        Assert.Equal("TCK-0041", await _tickets.NextReferenceAsync(CancellationToken.None));

    [Fact]
    public async Task NextReference_HasNoGap_WhenTheDatabaseSkippedIdentityValues()
    {
        // SQL Server caches identity values and skips ahead (by 1000 for int) after an unclean
        // restart, so ids have gaps. Customer-facing references must not inherit them.
        var ticket = Ticket.Raise("TCK-0041", "Raised after a restart", "The next id jumped.", 1, 1, TicketPriority.Low, _database.Clock.UtcNow)
            .WithId(1002);
        _database.Db.Add(ticket);
        await _database.Db.SaveChangesAsync();

        Assert.Equal(1002, await _database.Db.Set<Ticket>().MaxAsync(t => t.Id));
        Assert.Equal("TCK-0042", await _tickets.NextReferenceAsync(CancellationToken.None));
    }
}

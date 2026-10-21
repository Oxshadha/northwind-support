using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

/// <summary>
/// The ticket list's filters, run as real SQL against the development seed data (40 tickets),
/// so these tests prove the database does the filtering, counting and paging.
/// </summary>
public sealed class TicketQueriesTests : IAsyncLifetime
{
    private SeededDatabase _database = null!;
    private TicketQueries _queries = null!;

    private SupportDbContext _db => _database.Db;

    private FixedClock _clock => _database.Clock;

    public async Task InitializeAsync()
    {
        _database = await SeededDatabase.CreateAsync();
        _queries = new TicketQueries(_db, _clock, Policies.DefaultSla());
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task NoFilters_ReturnsEveryTicket()
    {
        var result = await Search(new TicketQuery());

        Assert.Equal(40, result.TotalCount);
    }

    [Fact]
    public async Task Priority_IsAnExactMatch()
    {
        var result = await Search(new TicketQuery { Priority = TicketPriority.High, PageSize = 100 });

        Assert.Equal(14, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal(TicketPriority.High, t.Priority));
    }

    [Fact]
    public async Task Status_IsAnExactMatch()
    {
        var result = await Search(new TicketQuery { Status = TicketStatus.Closed });

        Assert.Equal(6, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal(TicketStatus.Closed, t.Status));
    }

    [Fact]
    public async Task CategoryAndCustomer_AreExactMatches()
    {
        var result = await Search(new TicketQuery { CategoryId = 2, CustomerId = 4 });

        Assert.Equal(["TCK-0014", "TCK-0022"], References(result));
    }

    [Fact]
    public async Task Filters_CombineWithAnd()
    {
        var result = await Search(new TicketQuery
        {
            Status = TicketStatus.Open,
            Priority = TicketPriority.High,
            AssignedAgentId = 5
        });

        Assert.Equal(["TCK-0019"], References(result));
    }

    [Fact]
    public async Task UnassignedOnly_ReturnsTicketsWithoutAnAgent()
    {
        var result = await Search(new TicketQuery { UnassignedOnly = true });

        Assert.Equal(["TCK-0021", "TCK-0022", "TCK-0023", "TCK-0024"], References(result));
        Assert.All(result.Items, t => Assert.Null(t.AssignedAgent));
    }

    [Fact]
    public async Task UnassignedOnly_CombinesWithOtherFilters()
    {
        var result = await Search(new TicketQuery { UnassignedOnly = true, Priority = TicketPriority.Critical });

        Assert.Equal(["TCK-0021"], References(result));
    }

    [Fact]
    public async Task Search_MatchesTheCustomerName()
    {
        var result = await Search(new TicketQuery { Search = "Contoso" });

        Assert.Equal(7, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal("Contoso Ltd", t.Customer.Name));
    }

    [Fact]
    public async Task Search_MatchesTheReference()
    {
        var result = await Search(new TicketQuery { Search = "TCK-0013" });

        Assert.Equal(["TCK-0013"], References(result));
    }

    [Fact]
    public async Task Search_MatchesTheTitle()
    {
        var result = await Search(new TicketQuery { Search = "Safari" });

        Assert.Equal(["TCK-0004"], References(result));
    }

    [Fact]
    public async Task Search_IsTrimmed()
    {
        var result = await Search(new TicketQuery { Search = "  Contoso  " });

        Assert.Equal(7, result.TotalCount);
    }

    [Fact]
    public async Task Search_ThatIsBlank_IsIgnored()
    {
        var result = await Search(new TicketQuery { Search = "   " });

        Assert.Equal(40, result.TotalCount);
    }

    [Fact]
    public async Task Search_CombinesWithOtherFilters()
    {
        var result = await Search(new TicketQuery { Search = "Contoso", Status = TicketStatus.Resolved });

        Assert.Equal(["TCK-0025", "TCK-0029"], References(result));
    }

    [Fact]
    public async Task CountAndPages_DescribeTheFilteredSet_NotEveryTicket()
    {
        var result = await Search(new TicketQuery { Priority = TicketPriority.High, Page = 3, PageSize = 5 });

        Assert.Equal(14, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(4, result.Items.Count);
    }

    [Theory]
    [InlineData(SlaStatus.WithinSla)]
    [InlineData(SlaStatus.AtRisk)]
    [InlineData(SlaStatus.Breached)]
    [InlineData(SlaStatus.Met)]
    [InlineData(SlaStatus.NotApplicable)]
    public async Task SlaStatus_FilteredInSql_MatchesTheDomainRule_ForEveryTicket(SlaStatus status)
    {
        // Every ticket's status as the domain evaluates it (the list projection uses it)...
        var all = await Search(new TicketQuery { PageSize = 100 });
        var expected = all.Items.Where(t => t.SlaStatus == status).Select(t => t.Reference).Order();

        // ...must be exactly what the SQL filter returns.
        var filtered = await Search(new TicketQuery { SlaStatus = status, PageSize = 100 });

        Assert.Equal(expected, References(filtered));
        Assert.Equal(filtered.Items.Count, filtered.TotalCount);
    }

    [Fact]
    public async Task SlaStatus_SeedCoversEveryMeasurableState()
    {
        var all = await Search(new TicketQuery { PageSize = 100 });

        Assert.All(
            new[] { SlaStatus.WithinSla, SlaStatus.AtRisk, SlaStatus.Breached, SlaStatus.Met },
            status => Assert.Contains(all.Items, t => t.SlaStatus == status));
    }

    [Fact]
    public async Task SlaStatus_CombinesWithOtherFilters()
    {
        var atRisk = await Search(new TicketQuery { SlaStatus = SlaStatus.AtRisk, PageSize = 100 });
        var critical = atRisk.Items.Count(t => t.Priority == TicketPriority.Critical);

        var result = await Search(new TicketQuery { SlaStatus = SlaStatus.AtRisk, Priority = TicketPriority.Critical });

        Assert.Equal(critical, result.TotalCount);
    }

    [Fact]
    public async Task Escalations_AreListedNewestFirst_WithAgentNames()
    {
        var ticket = await _db.Set<Ticket>().SingleAsync(t => t.Reference == "TCK-0002");
        var sla = Policies.DefaultSla();
        ticket.Escalate("First chase from customer", "Team Lead", sla, CustomerTier.Standard, 1, _clock.UtcNow);
        _clock.Advance(TimeSpan.FromMinutes(10));
        ticket.Escalate("Second chase, now urgent", "Duty Manager", sla, CustomerTier.Standard, 2, _clock.UtcNow);
        await _db.SaveChangesAsync();

        var history = await _queries.GetEscalationsAsync(ticket.Id, CancellationToken.None);

        Assert.Equal(["Duty Manager", "Team Lead"], history.Select(e => e.EscalatedBy));
        Assert.Equal(TicketPriority.High, history[0].FromPriority);
        Assert.Equal(TicketPriority.Critical, history[0].ToPriority);
        Assert.Equal("Alex Turner", history[0].FromAgent?.FullName);
        Assert.Equal("Ben Osei", history[0].ToAgent?.FullName);
    }

    private Task<PagedResult<TicketListItemDto>> Search(TicketQuery query) =>
        _queries.GetPagedAsync(query.Normalized(), CancellationToken.None);

    private static string[] References(PagedResult<TicketListItemDto> result) =>
        [.. result.Items.Select(t => t.Reference).Order()];
}

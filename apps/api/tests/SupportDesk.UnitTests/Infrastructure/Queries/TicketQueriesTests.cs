using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
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
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FixedClock _clock = new();
    private SupportDbContext _db = null!;
    private TicketQueries _queries = null!;

    public async Task InitializeAsync()
    {
        // The in-memory database lives as long as this open connection.
        await _connection.OpenAsync();

        _db = new SupportDbContext(new DbContextOptionsBuilder<SupportDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        await new SupportDbSeeder(_db, _clock, NullLogger<SupportDbSeeder>.Instance).SeedAsync();

        _queries = new TicketQueries(_db, _clock);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

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

    private Task<PagedResult<TicketListItemDto>> Search(TicketQuery query) =>
        _queries.GetPagedAsync(query.Normalized(), CancellationToken.None);

    private static string[] References(PagedResult<TicketListItemDto> result) =>
        [.. result.Items.Select(t => t.Reference).Order()];
}

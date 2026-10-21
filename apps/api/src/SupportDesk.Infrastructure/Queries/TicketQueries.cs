using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Ticket reads. Projected in the database and never tracked. Aggregates hold only each
/// other's ids, so the customer, category and agent labels are joined in here, on the read side.
/// </summary>
public sealed class TicketQueries(SupportDbContext db, IClock clock, SlaPolicy sla) : ITicketQueries
{
    /// <remarks>
    /// Filters, counts, sorts and pages in that order, all in one database round trip each for
    /// the count and the page, so the count and the pager always describe the filtered set.
    /// </remarks>
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tickets = ApplyFilters(TicketsWithLabels(), query, now, sla.AtRiskThresholdPercent);

        var totalCount = await tickets.CountAsync(ct);

        var items = await ApplySort(tickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToListItem(now, sla.AtRiskThresholdPercent))
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var threshold = sla.AtRiskThresholdPercent;

        return TicketsWithLabels()
            .Where(x => x.Ticket.Id == id)
            .Select(x => new TicketDetailDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Description = x.Ticket.Description,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerContactDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Email, x.Customer.Phone, x.Customer.Tier),
                Category = new CategoryDto(x.Category.Id, x.Category.Name, x.Category.RequiresSpecialist),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = SlaEvaluator.Evaluate(
                    x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, x.Ticket.SlaWindowSeconds, threshold, now)
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct) =>
        await TicketsWithLabels()
            .Where(x => x.Ticket.CustomerId == customerId)
            .OrderByDescending(x => x.Ticket.CreatedAtUtc)
            .ThenByDescending(x => x.Ticket.Id)
            .Select(ToListItem(clock.UtcNow, sla.AtRiskThresholdPercent))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TicketEscalationDto>> GetEscalationsAsync(int ticketId, CancellationToken ct) =>
        await (
            from escalation in db.Set<TicketEscalation>().AsNoTracking()
            where escalation.TicketId == ticketId
            from fromAgent in db.Set<Agent>().Where(a => a.Id == escalation.FromAgentId).DefaultIfEmpty()
            from toAgent in db.Set<Agent>().Where(a => a.Id == escalation.ToAgentId).DefaultIfEmpty()
            orderby escalation.EscalatedAtUtc descending, escalation.Id descending
            select new TicketEscalationDto
            {
                Id = escalation.Id,
                FromPriority = escalation.FromPriority,
                ToPriority = escalation.ToPriority,
                FromAgent = fromAgent == null ? null : new AgentSummaryDto(fromAgent.Id, fromAgent.FullName),
                ToAgent = toAgent == null ? null : new AgentSummaryDto(toAgent.Id, toAgent.FullName),
                FromDueAtUtc = escalation.FromDueAtUtc,
                ToDueAtUtc = escalation.ToDueAtUtc,
                Reason = escalation.Reason,
                EscalatedBy = escalation.EscalatedBy,
                EscalatedAtUtc = escalation.EscalatedAtUtc
            })
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(int id, CancellationToken ct) =>
        db.Set<Ticket>().AsNoTracking().AnyAsync(t => t.Id == id, ct);

    /// <summary>
    /// One list row, as every ticket list shows it. Only the columns the row needs are read.
    /// </summary>
    private static Expression<Func<TicketWithLabels, TicketListItemDto>> ToListItem(DateTime now, int atRiskThresholdPercent) =>
        x => new TicketListItemDto
        {
            Id = x.Ticket.Id,
            Reference = x.Ticket.Reference,
            Title = x.Ticket.Title,
            Status = x.Ticket.Status,
            Priority = x.Ticket.Priority,
            Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
            Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
            AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
            CreatedAtUtc = x.Ticket.CreatedAtUtc,
            UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
            DueAtUtc = x.Ticket.DueAtUtc,
            ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
            SlaStatus = SlaEvaluator.Evaluate(
                x.Ticket.DueAtUtc, x.Ticket.ResolvedAtUtc, x.Ticket.SlaWindowSeconds, atRiskThresholdPercent, now)
        };

    /// <summary>
    /// Every ticket with the rows that label it. Filter and sort this, then project.
    /// </summary>
    private IQueryable<TicketWithLabels> TicketsWithLabels() =>
        from ticket in db.Set<Ticket>().AsNoTracking()
        join customer in db.Set<Customer>() on ticket.CustomerId equals customer.Id
        join category in db.Set<Category>() on ticket.CategoryId equals category.Id
        from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
        select new TicketWithLabels { Ticket = ticket, Customer = customer, Category = category, Agent = agent };

    /// <summary>
    /// Every supplied filter, combined with AND and translated to SQL. A new filter is one more
    /// line here.
    /// </summary>
    /// <remarks>
    /// Search uses <c>Contains</c>, which EF Core sends as a parameterised LIKE with any
    /// <c>%</c> or <c>_</c> in the text escaped; case-insensitivity comes from the database
    /// collation.
    /// </remarks>
    private static IQueryable<TicketWithLabels> ApplyFilters(
        IQueryable<TicketWithLabels> source,
        TicketQuery query,
        DateTime now,
        int atRiskThresholdPercent)
    {
        var search = query.Search?.Trim();

        return source
            .WhereIf(query.Status is not null, x => x.Ticket.Status == query.Status)
            .WhereIf(query.Priority is not null, x => x.Ticket.Priority == query.Priority)
            .WhereIf(query.CategoryId is not null, x => x.Ticket.CategoryId == query.CategoryId)
            .WhereIf(query.CustomerId is not null, x => x.Ticket.CustomerId == query.CustomerId)
            .WhereIf(query.AssignedAgentId is not null, x => x.Ticket.AssignedAgentId == query.AssignedAgentId)
            .WhereIf(query.UnassignedOnly, x => x.Ticket.AssignedAgentId == null)
            .WhereIf(!string.IsNullOrEmpty(search), x =>
                x.Ticket.Title.Contains(search!) ||
                x.Ticket.Reference.Contains(search!) ||
                x.Customer.Name.Contains(search!))
            .WhereIf(query.SlaStatus is not null, SlaSql.On(
                SlaSql.Is(query.SlaStatus.GetValueOrDefault(), now, atRiskThresholdPercent),
                (TicketWithLabels x) => x.Ticket));
    }

    private static IQueryable<TicketWithLabels> ApplySort(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "updatedatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.UpdatedAtUtc)
                : source.OrderBy(x => x.Ticket.UpdatedAtUtc),
            // SQL Server sorts NULL first; a ticket with no due date is the least urgent, so it
            // goes last whichever way the dates run.
            "dueatutc" => descending
                ? source.OrderBy(x => x.Ticket.DueAtUtc == null).ThenByDescending(x => x.Ticket.DueAtUtc)
                : source.OrderBy(x => x.Ticket.DueAtUtc == null).ThenBy(x => x.Ticket.DueAtUtc),
            "priority" => descending
                ? source.OrderByDescending(x => x.Ticket.Priority)
                : source.OrderBy(x => x.Ticket.Priority),
            "status" => descending
                ? source.OrderByDescending(x => x.Ticket.Status)
                : source.OrderBy(x => x.Ticket.Status),
            _ => descending
                ? source.OrderByDescending(x => x.Ticket.CreatedAtUtc)
                : source.OrderBy(x => x.Ticket.CreatedAtUtc)
        };

        // Keeps paging stable when the sort column has duplicates.
        return sorted.ThenByDescending(x => x.Ticket.Id);
    }

    private sealed class TicketWithLabels
    {
        public required Ticket Ticket { get; init; }

        public required Customer Customer { get; init; }

        public required Category Category { get; init; }

        public Agent? Agent { get; init; }
    }
}

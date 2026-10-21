using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class AgentRepository(SupportDbContext db) : IAgentRepository
{
    /// <summary>Loads the whole aggregate, specializations included.</summary>
    public Task<Agent?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Agent>()
            .Include(a => a.Specializations)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <remarks>
    /// Email is unique and compared by the database collation, so case does not matter.
    /// </remarks>
    public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct) =>
        db.Set<Agent>().FirstOrDefaultAsync(a => a.Email == email, ct);

    /// <remarks>
    /// One query for the whole team: open counts are a correlated count in SQL, not a query
    /// per agent. A support team is tens of agents, so the selection rules run in the domain
    /// over all of them rather than being duplicated as SQL filters.
    /// </remarks>
    public async Task<IReadOnlyList<AgentWorkload>> GetWorkloadsAsync(CancellationToken ct) =>
        await db.Set<Agent>()
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => new AgentWorkload(
                a.Id,
                a.FullName,
                a.IsActive,
                a.MaxOpenTickets,
                db.Set<Ticket>().Count(t =>
                    t.AssignedAgentId == a.Id &&
                    t.Status != TicketStatus.Resolved &&
                    t.Status != TicketStatus.Closed),
                a.Specializations.Select(s => s.CategoryId).ToList()))
            .ToListAsync(ct);
}

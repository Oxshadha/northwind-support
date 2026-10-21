using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

/// <summary>
/// Loads and stores <see cref="Ticket"/> aggregates. Loaded tickets are tracked, so the unit
/// of work saves whatever their methods changed.
/// </summary>
public sealed class TicketRepository(SupportDbContext db) : ITicketRepository
{
    public Task<Ticket?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Ticket>().FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <remarks>
    /// Follows the newest ticket's reference rather than its id: SQL Server may skip identity
    /// values (by 1000 after an unclean restart), and customers should not see the reference
    /// jump from TCK-0041 to TCK-1003. Callers issue references inside the assignment lock, so
    /// two requests cannot read the same last reference; the unique index backs that up.
    /// </remarks>
    public async Task<string> NextReferenceAsync(CancellationToken ct)
    {
        var lastReference = await db.Set<Ticket>()
            .AsNoTracking()
            .OrderByDescending(t => t.Id)
            .Select(t => t.Reference)
            .FirstOrDefaultAsync(ct);

        var lastNumber = lastReference is null
            ? 0
            : int.Parse(lastReference.AsSpan(ReferencePrefix.Length), CultureInfo.InvariantCulture);

        return $"{ReferencePrefix}{lastNumber + 1:D4}";
    }

    private const string ReferencePrefix = "TCK-";

    public async Task AddAsync(Ticket ticket, CancellationToken ct) =>
        await db.Set<Ticket>().AddAsync(ticket, ct);
}

using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;

/// <summary>
/// Returns a ticket's escalation history, newest first.
/// </summary>
public sealed class GetTicketEscalationsQueryHandler(ITicketQueries tickets)
{
    /// <exception cref="NotFoundException">No ticket has this id.</exception>
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int ticketId, CancellationToken ct)
    {
        var history = await tickets.GetEscalationsAsync(ticketId, ct);

        // An empty history is normal; it only means "not found" when the ticket is missing too.
        if (history.Count == 0 && !await tickets.ExistsAsync(ticketId, ct))
        {
            throw new NotFoundException("Ticket", ticketId);
        }

        return history;
    }
}

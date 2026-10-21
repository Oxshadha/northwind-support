using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

/// <summary>
/// Escalates a ticket: one priority level up, a new due date from now, the owner re-checked,
/// and a history row recording all of it.
/// </summary>
/// <remarks>
/// Uses the same rules as raising a ticket (<see cref="SlaPolicy"/> and
/// <see cref="AgentSelector"/>), so the two can never disagree.
/// </remarks>
public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    IUnitOfWork unitOfWork,
    IAssignmentLock assignmentLock,
    SlaPolicy sla,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ITicketQueries ticketQueries,
    ILogger<EscalateTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">No ticket has this id.</exception>
    /// <exception cref="BusinessRuleViolationException">The ticket is Critical, Resolved or Closed.</exception>
    public async Task<TicketEscalatedDto> HandleAsync(int id, EscalateTicketRequest request, CancellationToken ct)
    {
        var (escalationId, reference, assignment) = await assignmentLock.RunExclusiveAsync(async token =>
        {
            var ticket = await tickets.GetByIdAsync(id, token) ?? throw new NotFoundException("Ticket", id);

            // Fail before reading anything else when the answer is already no.
            ticket.EnsureCanBeEscalated();

            var customer = await customers.GetByIdAsync(ticket.CustomerId, token)
                ?? throw new InvalidOperationException($"Ticket {ticket.Reference} refers to a missing customer.");

            var category = await categories.GetByIdAsync(ticket.CategoryId, token)
                ?? throw new InvalidOperationException($"Ticket {ticket.Reference} refers to a missing category.");

            var decision = AgentSelector.Reevaluate(ticket.AssignedAgentId, await agents.GetWorkloadsAsync(token), category);

            var escalation = ticket.Escalate(
                request.Reason, request.EscalatedBy, sla, customer.Tier, decision.AgentId, clock.UtcNow);

            await unitOfWork.SaveChangesAsync(token);

            return (escalation.Id, ticket.Reference, decision);
        }, ct);

        logger.LogInformation(
            "Ticket {Reference} escalated by {EscalatedBy}. {Assignment}",
            reference, request.EscalatedBy, assignment.Reason);

        var history = await ticketQueries.GetEscalationsAsync(id, ct);

        return new TicketEscalatedDto(
            await getTicket.HandleAsync(id, ct),
            history.First(e => e.Id == escalationId));
    }
}

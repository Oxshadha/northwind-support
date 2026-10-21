using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket and triages it: decides its priority, starts its response clock and gives
/// it an owner, then explains each decision.
/// </summary>
/// <remarks>
/// The rules live in the domain (<see cref="PriorityPolicy"/>, <see cref="SlaPolicy"/>,
/// <see cref="AgentSelector"/>); this handler only gathers what they need and saves the result.
/// Finding nobody to assign is a normal outcome: the ticket is raised unassigned, with the reason.
/// </remarks>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    IUnitOfWork unitOfWork,
    IAssignmentLock assignmentLock,
    SlaPolicy sla,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    /// <exception cref="ConflictException">The category is no longer in use.</exception>
    public async Task<RaisedTicketDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var category = await categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException("Category", request.CategoryId);

        if (!category.IsActive)
        {
            throw new ConflictException($"Category {category.Name} is no longer in use for new tickets.");
        }

        var priority = PriorityPolicy.Decide(request.RequestedPriority, category);
        var window = sla.WindowFor(priority.Priority, customer.Tier);

        // Reading workloads, choosing an owner and saving happen as one exclusive step, so a
        // concurrent request cannot hand the same agent's last free slot to another ticket.
        var (ticketId, reference, assignment) = await assignmentLock.RunExclusiveAsync(async token =>
        {
            var now = clock.UtcNow;

            var ticket = Ticket.Raise(
                await tickets.NextReferenceAsync(token),
                request.Title,
                request.Description,
                customer.Id,
                category.Id,
                priority.Priority,
                now);

            ticket.StartSla(now, window);

            var decision = AgentSelector.Select(await agents.GetWorkloadsAsync(token), category);
            ticket.AssignTo(decision.AgentId, now);

            await tickets.AddAsync(ticket, token);
            await unitOfWork.SaveChangesAsync(token);

            return (ticket.Id, ticket.Reference, decision);
        }, ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}: {Priority}, agent {AgentId}.",
            reference, customer.Id, category.Id, priority.Priority, assignment.AgentId);

        return new RaisedTicketDto(
            await getTicket.HandleAsync(ticketId, ct),
            new TriageDto(priority.Reason, sla.Explain(priority.Priority, customer.Tier), assignment.Reason));
    }
}

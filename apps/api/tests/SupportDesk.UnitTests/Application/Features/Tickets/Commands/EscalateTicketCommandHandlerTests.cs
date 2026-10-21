using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class EscalateTicketCommandHandlerTests
{
    private static readonly EscalateTicketRequest Request = new("Customer has chased twice", "Team Lead");

    private static Ticket OpenTicket(TicketPriority priority, int? agentId)
    {
        var ticket = new TicketBuilder().WithPriority(priority).WithStatus(TicketStatus.Open).AssignedTo(agentId).Build();
        ticket.StartSla(FixedClock.DefaultNow, TimeSpan.FromHours(24));
        return ticket;
    }

    [Fact]
    public async Task HandleAsync_RaisesPriority_RestartsTheClockFromNow_AndRecordsHistory()
    {
        var context = new TicketCommandTestContext();
        context.Clock.Advance(TimeSpan.FromHours(5));
        var ticket = context.ExistingTicket(OpenTicket(TicketPriority.Medium, agentId: 1));

        await context.EscalateTicket.HandleAsync(ticket.Id, Request, CancellationToken.None);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(context.Clock.UtcNow.AddHours(8), ticket.DueAtUtc);
        var escalation = Assert.Single(ticket.Escalations);
        Assert.Equal("Team Lead", escalation.EscalatedBy);
        context.VerifySaved(Times.Once());
        Assert.Equal(1, context.AssignmentLock.Runs);
    }

    [Fact]
    public async Task HandleAsync_UsesThePremiumWindow_ForAPremiumCustomer()
    {
        var context = new TicketCommandTestContext();
        context.WithCustomer(CustomerTier.Premium);
        var ticket = context.ExistingTicket(OpenTicket(TicketPriority.Medium, agentId: 1));

        await context.EscalateTicket.HandleAsync(ticket.Id, Request, CancellationToken.None);

        Assert.Equal(context.Clock.UtcNow.AddHours(4), ticket.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_KeepsAnOwnerWhoIsStillEligible_EvenAtTheirLimit()
    {
        var context = new TicketCommandTestContext();
        context.WithCategory(Policies.Billing());
        var ticket = context.ExistingTicket(OpenTicket(TicketPriority.Medium, agentId: 5)); // Marcus, 6/6

        await context.EscalateTicket.HandleAsync(ticket.Id, Request, CancellationToken.None);

        Assert.Equal(5, ticket.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_ReassignsWhenTheOwnerIsNoLongerEligible()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(OpenTicket(TicketPriority.Medium, agentId: 6)); // Yuki, inactive

        await context.EscalateTicket.HandleAsync(ticket.Id, Request, CancellationToken.None);

        Assert.Equal(4, ticket.AssignedAgentId); // Sara, fewest open among active agents
        var escalation = Assert.Single(ticket.Escalations);
        Assert.Equal(6, escalation.FromAgentId);
        Assert.Equal(4, escalation.ToAgentId);
    }

    [Fact]
    public async Task HandleAsync_WithNobodyEligible_LeavesTheTicketUnassigned()
    {
        var context = new TicketCommandTestContext();
        context.WithCategory(Policies.Billing());
        context.WithWorkloads(new AgentWorkload(5, "Marcus Doyle", true, 6, 6, [2]));
        var ticket = context.ExistingTicket(OpenTicket(TicketPriority.Low, agentId: null));

        await context.EscalateTicket.HandleAsync(ticket.Id, Request, CancellationToken.None);

        Assert.Null(ticket.AssignedAgentId);
        Assert.Equal(TicketPriority.Medium, ticket.Priority);
    }

    [Fact]
    public async Task HandleAsync_ForAnUnknownTicket_IsNotFound()
    {
        var context = new TicketCommandTestContext();

        await Assert.ThrowsAsync<NotFoundException>(
            () => context.EscalateTicket.HandleAsync(999, Request, CancellationToken.None));

        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_ForACriticalTicket_IsRejected_BeforeReadingAnythingElse()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(OpenTicket(TicketPriority.Critical, agentId: 1));

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => context.EscalateTicket.HandleAsync(ticket.Id, Request, CancellationToken.None));

        Assert.Contains("already Critical", error.Message);
        context.Agents.Verify(a => a.GetWorkloadsAsync(It.IsAny<CancellationToken>()), Times.Never());
        context.VerifySaved(Times.Never());
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task HandleAsync_ForAResolvedOrClosedTicket_IsRejected(TicketStatus status)
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(new TicketBuilder().WithStatus(status).Build());

        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => context.EscalateTicket.HandleAsync(ticket.Id, Request, CancellationToken.None));

        Assert.Contains($"is {status}", error.Message);
        context.VerifySaved(Times.Never());
    }
}

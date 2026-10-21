using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_InAForcedCriticalCategory_IsCritical_WhateverWasRequested()
    {
        var context = new TicketCommandTestContext();
        context.WithCategory(Policies.Security());

        var raised = await context.RaiseTicket.HandleAsync(Request(TicketPriority.Low), CancellationToken.None);

        Assert.Equal(TicketPriority.Critical, context.AddedTicket!.Priority);
        Assert.Contains("always Critical", raised.Triage.Priority);
    }

    [Theory]
    [InlineData(CustomerTier.Standard, 8)]
    [InlineData(CustomerTier.Premium, 4)]
    public async Task HandleAsync_StartsTheResponseClock_ForThePriorityAndTier(CustomerTier tier, int hours)
    {
        var context = new TicketCommandTestContext();
        context.WithCustomer(tier);

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        Assert.Equal(FixedClock.DefaultNow.AddHours(hours), context.AddedTicket!.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_AssignsTheEligibleAgentWithFewestOpenTickets_InsideTheLock()
    {
        var context = new TicketCommandTestContext();
        context.WithCategory(Policies.Billing());

        var raised = await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        // Billing specialists: Sara (2/6) and Marcus (6/6, full).
        Assert.Equal(4, context.AddedTicket!.AssignedAgentId);
        Assert.Contains("Sara Lindqvist", raised.Triage.Assignment);
        Assert.Equal(1, context.AssignmentLock.Runs);
    }

    [Fact]
    public async Task HandleAsync_WithNobodyEligible_RaisesTheTicketUnassigned_WithTheReason()
    {
        var context = new TicketCommandTestContext();
        context.WithCategory(Policies.Billing());
        context.WithWorkloads(new AgentWorkload(5, "Marcus Doyle", true, 6, 6, [2]));

        var raised = await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.Null(context.AddedTicket!.AssignedAgentId);
        Assert.StartsWith("Unassigned", raised.Triage.Assignment);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_ExplainsEveryDecision()
    {
        var context = new TicketCommandTestContext();

        var raised = await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        Assert.Equal("High, as requested.", raised.Triage.Priority);
        Assert.Equal("High priority: 8 h response window.", raised.Triage.SlaWindow);
        Assert.NotEmpty(raised.Triage.Assignment);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_InAnInactiveCategory_IsAConflict()
    {
        var context = new TicketCommandTestContext();
        var retired = new Category("Legacy", requiresSpecialist: false, forcesCriticalPriority: false).WithId(9);
        typeof(Category).GetProperty(nameof(Category.IsActive))!.SetValue(retired, false);
        context.WithCategory(retired);

        await Assert.ThrowsAsync<ConflictException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        context.VerifySaved(Times.Never());
    }
}

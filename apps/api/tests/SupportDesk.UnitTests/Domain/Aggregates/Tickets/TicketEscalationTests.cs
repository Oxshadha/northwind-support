using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class TicketEscalationTests
{
    private static readonly DateTime Later = FixedClock.DefaultNow.AddHours(3);
    private static readonly SlaPolicy Sla = Policies.DefaultSla();

    private static Ticket OpenTicket(TicketPriority priority, int? agentId = 4)
    {
        var ticket = new TicketBuilder().WithPriority(priority).WithStatus(TicketStatus.Open).AssignedTo(agentId).Build();
        ticket.StartSla(FixedClock.DefaultNow, Sla.WindowFor(priority, CustomerTier.Standard));
        return ticket;
    }

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void Escalate_RaisesThePriorityByExactlyOneLevel(TicketPriority from, TicketPriority to)
    {
        var ticket = OpenTicket(from);

        ticket.Escalate("Customer chased twice", "Team Lead", Sla, CustomerTier.Standard, 4, Later);

        Assert.Equal(to, ticket.Priority);
    }

    [Theory]
    [InlineData(CustomerTier.Standard, 8)]
    [InlineData(CustomerTier.Premium, 4)]
    public void Escalate_RecomputesTheDueDateFromTheMomentOfEscalation(CustomerTier tier, int hours)
    {
        var ticket = OpenTicket(TicketPriority.Medium);

        ticket.Escalate("Customer chased twice", "Team Lead", Sla, tier, 4, Later);

        // Now High: due one High window after the escalation, not after creation.
        Assert.Equal(Later.AddHours(hours), ticket.DueAtUtc);
        Assert.Equal(hours * 3600, ticket.SlaWindowSeconds);
    }

    [Fact]
    public void Escalate_KeepsOrChangesTheOwner_AsTheCallerDecided()
    {
        var kept = OpenTicket(TicketPriority.Medium, agentId: 4);
        var changed = OpenTicket(TicketPriority.Medium, agentId: 4);

        kept.Escalate("Customer chased twice", "Team Lead", Sla, CustomerTier.Standard, 4, Later);
        changed.Escalate("Customer chased twice", "Team Lead", Sla, CustomerTier.Standard, 2, Later);

        Assert.Equal(4, kept.AssignedAgentId);
        Assert.Equal(2, changed.AssignedAgentId);
    }

    [Fact]
    public void Escalate_RecordsTheBeforeAndAfter_WhoAndWhy()
    {
        var ticket = OpenTicket(TicketPriority.Medium, agentId: 4);
        var dueBefore = ticket.DueAtUtc;

        var escalation = ticket.Escalate("  Customer chased twice  ", " Team Lead ", Sla, CustomerTier.Standard, 2, Later);

        Assert.Equal(TicketPriority.Medium, escalation.FromPriority);
        Assert.Equal(TicketPriority.High, escalation.ToPriority);
        Assert.Equal(4, escalation.FromAgentId);
        Assert.Equal(2, escalation.ToAgentId);
        Assert.Equal(dueBefore, escalation.FromDueAtUtc);
        Assert.Equal(Later.AddHours(8), escalation.ToDueAtUtc);
        Assert.Equal("Customer chased twice", escalation.Reason);
        Assert.Equal("Team Lead", escalation.EscalatedBy);
        Assert.Equal(Later, escalation.EscalatedAtUtc);
        Assert.Same(escalation, Assert.Single(ticket.Escalations));
        Assert.Equal(Later, ticket.UpdatedAtUtc);
    }

    [Fact]
    public void Escalate_ACriticalTicket_IsRejected_AndChangesNothing()
    {
        var ticket = OpenTicket(TicketPriority.Critical);

        var error = Assert.Throws<BusinessRuleViolationException>(() =>
            ticket.Escalate("Customer chased twice", "Team Lead", Sla, CustomerTier.Standard, 2, Later));

        Assert.Contains("already Critical", error.Message);
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
        Assert.Equal(4, ticket.AssignedAgentId);
        Assert.Empty(ticket.Escalations);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Escalate_AResolvedOrClosedTicket_IsRejected(TicketStatus status)
    {
        var ticket = new TicketBuilder().WithPriority(TicketPriority.Low).WithStatus(status).Build();

        var error = Assert.Throws<BusinessRuleViolationException>(() =>
            ticket.Escalate("Customer chased twice", "Team Lead", Sla, CustomerTier.Standard, 2, Later));

        Assert.Contains($"is {status}", error.Message);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Empty(ticket.Escalations);
    }

    [Fact]
    public void StartSla_SetsTheDueDateOneWindowLater()
    {
        var ticket = new TicketBuilder().Build();

        ticket.StartSla(FixedClock.DefaultNow, TimeSpan.FromHours(2));

        Assert.Equal(FixedClock.DefaultNow.AddHours(2), ticket.DueAtUtc);
        Assert.Equal(7200, ticket.SlaWindowSeconds);
    }
}

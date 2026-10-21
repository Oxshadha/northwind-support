using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class PriorityPolicyTests
{
    [Fact]
    public void UsesTheRequestedPriority() =>
        Assert.Equal(TicketPriority.Low, PriorityPolicy.Decide(TicketPriority.Low, Policies.General()).Priority);

    [Fact]
    public void DefaultsToMedium() =>
        Assert.Equal(TicketPriority.Medium, PriorityPolicy.Decide(null, Policies.General()).Priority);

    [Theory]
    [InlineData(null)]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.High)]
    public void AForcedCriticalCategory_IsAlwaysCritical(TicketPriority? requested)
    {
        var decision = PriorityPolicy.Decide(requested, Policies.Security());

        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Contains("Security tickets are always Critical", decision.Reason);
    }
}

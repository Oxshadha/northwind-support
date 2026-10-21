using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class SlaPolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Critical, CustomerTier.Standard, 4)]
    [InlineData(TicketPriority.High, CustomerTier.Standard, 8)]
    [InlineData(TicketPriority.Medium, CustomerTier.Standard, 24)]
    [InlineData(TicketPriority.Low, CustomerTier.Standard, 72)]
    [InlineData(TicketPriority.Critical, CustomerTier.Premium, 2)]
    [InlineData(TicketPriority.High, CustomerTier.Premium, 4)]
    [InlineData(TicketPriority.Medium, CustomerTier.Premium, 12)]
    [InlineData(TicketPriority.Low, CustomerTier.Premium, 36)]
    public void WindowFor_IsTheBaseWindow_HalvedForPremium(TicketPriority priority, CustomerTier tier, double hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), Policies.DefaultSla().WindowFor(priority, tier));

    [Fact]
    public void WindowFor_Premium_IsNeverShorterThanTheMinimum()
    {
        // 1.5 h halved is 45 min, under the 1 h floor. The default settings never reach the
        // floor (the shortest premium window is 2 h), so the rule needs its own settings.
        var sla = Policies.Sla(criticalHours: 1.5);

        Assert.Equal(TimeSpan.FromHours(1), sla.WindowFor(TicketPriority.Critical, CustomerTier.Premium));
    }

    [Fact]
    public void WindowFor_Premium_AtExactlyTheMinimum_IsTheMinimum()
    {
        var sla = Policies.Sla(criticalHours: 2);

        Assert.Equal(TimeSpan.FromHours(1), sla.WindowFor(TicketPriority.Critical, CustomerTier.Premium));
    }

    [Fact]
    public void WindowFor_UsesTheConfiguredMultiplier()
    {
        var sla = Policies.Sla(premiumMultiplier: 0.25);

        Assert.Equal(TimeSpan.FromHours(6), sla.WindowFor(TicketPriority.Medium, CustomerTier.Premium));
    }

    [Fact]
    public void Constructor_RejectsAMissingPriority()
    {
        var windows = new Dictionary<TicketPriority, TimeSpan> { [TicketPriority.Low] = TimeSpan.FromHours(72) };

        Assert.Throws<ArgumentException>(() => new SlaPolicy(windows, 0.5, TimeSpan.FromHours(1), 25));
    }

    [Fact]
    public void Constructor_RejectsABaseWindowShorterThanTheMinimum() =>
        Assert.Throws<ArgumentException>(() => Policies.Sla(criticalHours: 0.5));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Constructor_RejectsAThresholdOutsideZeroToHundred(int percent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Policies.Sla(atRiskThresholdPercent: percent));

    [Fact]
    public void Explain_SaysWhenTheFloorApplied()
    {
        var reason = Policies.Sla(criticalHours: 1.5).Explain(TicketPriority.Critical, CustomerTier.Premium);

        Assert.Contains("1 h minimum", reason);
    }
}

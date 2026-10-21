using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Application.Features.Reports.Queries.GetSlaSummary;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Reports;

public class SlaSummaryTests
{
    private readonly SlaSummaryRequestValidator _validator = new();

    [Theory]
    [InlineData("agent")]
    [InlineData("Priority")]
    public void GroupBy_AcceptsAgentOrPriority_InAnyCase(string groupBy) =>
        Assert.True(_validator.Validate(new SlaSummaryRequest(null, null, groupBy)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("team")]
    public void GroupBy_RejectsAnythingElse(string? groupBy) =>
        Assert.False(_validator.Validate(new SlaSummaryRequest(null, null, groupBy)).IsValid);

    [Fact]
    public void From_MustBeBeforeTo()
    {
        var day = FixedClock.DefaultNow;

        Assert.False(_validator.Validate(new SlaSummaryRequest(day, day, "agent")).IsValid);
        Assert.False(_validator.Validate(new SlaSummaryRequest(day.AddDays(1), day, "agent")).IsValid);
        Assert.True(_validator.Validate(new SlaSummaryRequest(day, day.AddDays(1), "agent")).IsValid);
    }

    [Fact]
    public async Task Period_DefaultsToTheLast30Days_EndingNow()
    {
        var reports = new Mock<IReportQueries>();
        var handler = new GetSlaSummaryQueryHandler(reports.Object, new FixedClock());

        var summary = await handler.HandleAsync(new SlaSummaryRequest(null, null, "priority"), CancellationToken.None);

        Assert.Equal(FixedClock.DefaultNow, summary.ToUtc);
        Assert.Equal(FixedClock.DefaultNow.AddDays(-30), summary.FromUtc);
        reports.Verify(r => r.GetSlaSummaryAsync(
            FixedClock.DefaultNow.AddDays(-30), FixedClock.DefaultNow, SlaSummaryGrouping.Priority, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Period_FromTheQueryString_IsReadAsUtc()
    {
        var reports = new Mock<IReportQueries>();
        var handler = new GetSlaSummaryQueryHandler(reports.Object, new FixedClock());

        var summary = await handler.HandleAsync(
            new SlaSummaryRequest(new DateTime(2026, 10, 1), new DateTime(2026, 11, 1), "agent"), CancellationToken.None);

        Assert.Equal(DateTimeKind.Utc, summary.FromUtc.Kind);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), summary.FromUtc);
    }

    [Theory]
    [InlineData(10, 0, 3, 30.0)]   // 3 of 10
    [InlineData(10, 4, 3, 50.0)]   // tickets without an SLA are left out: 3 of 6
    [InlineData(3, 0, 1, 33.3)]    // one decimal place
    [InlineData(3, 0, 2, 66.7)]
    [InlineData(5, 0, 0, 0.0)]
    public void BreachRate_IsBreachedOverTicketsWithAnSla(int total, int noSla, int breached, double expected) =>
        Assert.Equal(expected, new SlaSummaryGroupDto { Total = total, NoSla = noSla, Breached = breached }.BreachRatePercent);

    [Fact]
    public void BreachRate_IsNull_WhenNoTicketHasAnSla() =>
        Assert.Null(new SlaSummaryGroupDto { Total = 2, NoSla = 2 }.BreachRatePercent);
}

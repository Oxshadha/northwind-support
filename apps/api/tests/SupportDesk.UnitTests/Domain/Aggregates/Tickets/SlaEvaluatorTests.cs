using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class SlaEvaluatorTests
{
    private const int FourHours = 4 * 60 * 60;
    private const int Threshold = 25;
    private static readonly DateTime Now = FixedClock.DefaultNow;

    private static SlaStatus Evaluate(DateTime? due, DateTime? resolved = null, int? window = FourHours) =>
        SlaEvaluator.Evaluate(due, resolved, window, Threshold, Now);

    [Fact]
    public void WithoutADueDate_IsNotApplicable() =>
        Assert.Equal(SlaStatus.NotApplicable, Evaluate(due: null));

    [Fact]
    public void ResolvedBeforeDue_IsMet() =>
        Assert.Equal(SlaStatus.Met, Evaluate(due: Now, resolved: Now.AddMinutes(-1)));

    [Fact]
    public void ResolvedExactlyAtDue_IsMet() =>
        Assert.Equal(SlaStatus.Met, Evaluate(due: Now, resolved: Now));

    [Fact]
    public void ResolvedLate_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, Evaluate(due: Now, resolved: Now.AddSeconds(1)));

    [Fact]
    public void OpenAndPastDue_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, Evaluate(due: Now.AddSeconds(-1)));

    [Fact]
    public void OpenAndExactlyDue_IsAtRisk_NotYetBreached() =>
        Assert.Equal(SlaStatus.AtRisk, Evaluate(due: Now));

    [Fact]
    public void OpenWithExactlyTheThresholdRemaining_IsAtRisk() =>
        // 25 % of 4 h is 1 h.
        Assert.Equal(SlaStatus.AtRisk, Evaluate(due: Now.AddHours(1)));

    [Fact]
    public void OpenWithJustOverTheThresholdRemaining_IsWithinSla() =>
        Assert.Equal(SlaStatus.WithinSla, Evaluate(due: Now.AddHours(1).AddSeconds(1)));

    [Fact]
    public void OpenWithPlentyOfTimeRemaining_IsWithinSla() =>
        Assert.Equal(SlaStatus.WithinSla, Evaluate(due: Now.AddHours(3)));

    [Fact]
    public void OpenWithoutAKnownWindow_IsNeverAtRisk() =>
        Assert.Equal(SlaStatus.WithinSla, Evaluate(due: Now.AddMinutes(1), window: null));
}

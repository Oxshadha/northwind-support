using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// The response-window rules: how long the team has to respond to a ticket of a given priority
/// for a given customer, and when a ticket counts as at risk. Every number comes from
/// configuration, so the rules change without a code change.
/// </summary>
public sealed class SlaPolicy
{
    private readonly IReadOnlyDictionary<TicketPriority, TimeSpan> _baseWindows;

    /// <param name="baseWindows">The window for each priority, for a standard customer.</param>
    /// <param name="premiumMultiplier">Applied to a premium customer's window, e.g. 0.5 for half.</param>
    /// <param name="minimumWindow">No premium window is shorter than this.</param>
    /// <param name="atRiskThresholdPercent">An open ticket with this share of its window or less remaining is at risk.</param>
    /// <exception cref="ArgumentException">A value is missing or out of range.</exception>
    public SlaPolicy(
        IReadOnlyDictionary<TicketPriority, TimeSpan> baseWindows,
        double premiumMultiplier,
        TimeSpan minimumWindow,
        int atRiskThresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(baseWindows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(premiumMultiplier);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(atRiskThresholdPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(atRiskThresholdPercent, 100);

        foreach (var priority in Enum.GetValues<TicketPriority>())
        {
            if (!baseWindows.TryGetValue(priority, out var window) || window < minimumWindow)
            {
                throw new ArgumentException(
                    $"The {priority} window must be set and no shorter than the {minimumWindow} minimum.",
                    nameof(baseWindows));
            }
        }

        _baseWindows = new Dictionary<TicketPriority, TimeSpan>(baseWindows);
        PremiumMultiplier = premiumMultiplier;
        MinimumWindow = minimumWindow;
        AtRiskThresholdPercent = atRiskThresholdPercent;
    }

    public double PremiumMultiplier { get; }

    public TimeSpan MinimumWindow { get; }

    public int AtRiskThresholdPercent { get; }

    /// <summary>The response window for this priority and customer tier.</summary>
    public TimeSpan WindowFor(TicketPriority priority, CustomerTier tier)
    {
        var window = _baseWindows[priority];

        if (tier != CustomerTier.Premium)
        {
            return window;
        }

        var shortened = window * PremiumMultiplier;

        return shortened < MinimumWindow ? MinimumWindow : shortened;
    }

    /// <summary>Why the window is what it is, in words a team lead would use.</summary>
    public string Explain(TicketPriority priority, CustomerTier tier)
    {
        var baseHours = _baseWindows[priority].TotalHours;
        var window = WindowFor(priority, tier);

        if (tier != CustomerTier.Premium)
        {
            return $"{priority} priority: {Hours(window)} response window.";
        }

        var floored = window == MinimumWindow && _baseWindows[priority] * PremiumMultiplier < MinimumWindow;

        return floored
            ? $"{priority} priority: {baseHours:0.##} h window, shortened for a premium customer and raised to the {Hours(MinimumWindow)} minimum."
            : $"{priority} priority: {baseHours:0.##} h window, shortened to {Hours(window)} for a premium customer.";
    }

    private static string Hours(TimeSpan span) => $"{span.TotalHours:0.##} h";
}

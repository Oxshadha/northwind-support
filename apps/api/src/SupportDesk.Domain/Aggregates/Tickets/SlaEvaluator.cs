namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Works out where a ticket stands against its due date. SLA status is always derived from
/// the ticket's dates and the current time - never stored.
/// </summary>
/// <remarks>
/// The ticket list filters on SLA status in SQL; that translation must give the same answer
/// as this method, which a parity test over the seed data checks.
/// </remarks>
public static class SlaEvaluator
{
    /// <param name="dueAtUtc">When the response is due; null when no SLA applies.</param>
    /// <param name="resolvedAtUtc">When the ticket was resolved; null while it is open.</param>
    /// <param name="slaWindowSeconds">The length of the window that ends at <paramref name="dueAtUtc"/>.</param>
    /// <param name="atRiskThresholdPercent">At risk with this share of the window or less remaining.</param>
    /// <param name="nowUtc">The current time, injected so the boundaries can be tested.</param>
    public static SlaStatus Evaluate(
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        int? slaWindowSeconds,
        int atRiskThresholdPercent,
        DateTime nowUtc)
    {
        if (dueAtUtc is not { } due)
        {
            return SlaStatus.NotApplicable;
        }

        if (resolvedAtUtc is { } resolved)
        {
            return resolved <= due ? SlaStatus.Met : SlaStatus.Breached;
        }

        if (nowUtc > due)
        {
            return SlaStatus.Breached;
        }

        return IsAtRisk(due, slaWindowSeconds, atRiskThresholdPercent, nowUtc)
            ? SlaStatus.AtRisk
            : SlaStatus.WithinSla;
    }

    /// <summary>
    /// True from the moment only the threshold share of the window is left, up to and
    /// including the due time itself.
    /// </summary>
    private static bool IsAtRisk(DateTime due, int? slaWindowSeconds, int thresholdPercent, DateTime nowUtc) =>
        slaWindowSeconds is { } window &&
        nowUtc >= due.AddSeconds(-AtRiskSeconds(window, thresholdPercent));

    /// <summary>How long before the due time a ticket becomes at risk.</summary>
    public static double AtRiskSeconds(int slaWindowSeconds, int thresholdPercent) =>
        slaWindowSeconds * thresholdPercent / 100.0;
}

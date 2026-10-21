using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>The rules as the development configuration and seed data define them.</summary>
public static class Policies
{
    /// <summary>The <c>Sla</c> section of appsettings.Development.json.</summary>
    public static SlaPolicy DefaultSla() => Sla(premiumMultiplier: 0.5, minimumWindowHours: 1);

    public static SlaPolicy Sla(
        double premiumMultiplier = 0.5,
        double minimumWindowHours = 1,
        double criticalHours = 4,
        int atRiskThresholdPercent = 25) =>
        new(
            new Dictionary<TicketPriority, TimeSpan>
            {
                [TicketPriority.Low] = TimeSpan.FromHours(72),
                [TicketPriority.Medium] = TimeSpan.FromHours(24),
                [TicketPriority.High] = TimeSpan.FromHours(8),
                [TicketPriority.Critical] = TimeSpan.FromHours(criticalHours)
            },
            premiumMultiplier,
            TimeSpan.FromHours(minimumWindowHours),
            atRiskThresholdPercent);

    public static Category General() => new Category("General", requiresSpecialist: false, forcesCriticalPriority: false).WithId(1);

    public static Category Billing() => new Category("Billing", requiresSpecialist: true, forcesCriticalPriority: false).WithId(2);

    public static Category Technical() => new Category("Technical", requiresSpecialist: false, forcesCriticalPriority: false).WithId(3);

    public static Category Security() => new Category("Security", requiresSpecialist: true, forcesCriticalPriority: true).WithId(5);
}

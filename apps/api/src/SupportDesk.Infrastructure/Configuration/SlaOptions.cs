using Microsoft.Extensions.Options;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Configuration;

/// <summary>
/// The <c>Sla</c> configuration section: the numbers behind <see cref="SlaPolicy"/>. Kept in
/// configuration so a team lead's change of policy needs no code change or release.
/// </summary>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    public Dictionary<TicketPriority, double> BaseWindowHours { get; init; } = [];

    public double PremiumCustomerMultiplier { get; init; }

    public double MinimumWindowHours { get; init; }

    public int AtRiskThresholdPercent { get; init; }

    /// <exception cref="ArgumentException">A value is missing or out of range.</exception>
    public SlaPolicy ToPolicy() =>
        new(
            BaseWindowHours.ToDictionary(pair => pair.Key, pair => TimeSpan.FromHours(pair.Value)),
            PremiumCustomerMultiplier,
            TimeSpan.FromHours(MinimumWindowHours),
            AtRiskThresholdPercent);
}

/// <summary>
/// Fails start-up on a bad <c>Sla</c> section, saying what is wrong, rather than failing the
/// first request that needs it.
/// </summary>
internal sealed class SlaOptionsValidator : IValidateOptions<SlaOptions>
{
    public ValidateOptionsResult Validate(string? name, SlaOptions options)
    {
        try
        {
            options.ToPolicy();
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException exception)
        {
            return ValidateOptionsResult.Fail($"Configuration section '{SlaOptions.SectionName}' is invalid: {exception.Message}");
        }
    }
}

using FluentValidation;
using SupportDesk.Application.Contracts.Reports;

namespace SupportDesk.Application.Features.Reports.Queries.GetSlaSummary;

public sealed class SlaSummaryRequestValidator : AbstractValidator<SlaSummaryRequest>
{
    public SlaSummaryRequestValidator()
    {
        RuleFor(x => x.GroupBy)
            .NotEmpty()
            .Must(value => Enum.TryParse<SlaSummaryGrouping>(value, ignoreCase: true, out _))
            .WithMessage("GroupBy must be 'agent' or 'priority'.");

        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From < x.To)
            .WithName("From")
            .WithMessage("From must be earlier than To.");
    }
}

using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        // Measured after trimming, as stored: five spaces are not a reason.
        RuleFor(x => x.Reason)
            .NotEmpty()
            .Must(reason => reason.Trim().Length is >= TicketEscalation.ReasonMinLength and <= TicketEscalation.ReasonMaxLength)
            .WithMessage($"Reason must be between {TicketEscalation.ReasonMinLength} and {TicketEscalation.ReasonMaxLength} characters.");
    }
}

using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class EscalateTicketRequestValidatorTests
{
    private readonly EscalateTicketRequestValidator _validator = new();

    [Theory]
    [InlineData(5)]
    [InlineData(500)]
    public void AReasonAtEitherLimit_IsValid(int length) =>
        Assert.True(_validator.Validate(new EscalateTicketRequest(new string('x', length), "Team Lead")).IsValid);

    [Theory]
    [InlineData(4)]
    [InlineData(501)]
    public void AReasonOutsideTheLimits_IsInvalid(int length) =>
        Assert.False(_validator.Validate(new EscalateTicketRequest(new string('x', length), "Team Lead")).IsValid);

    [Fact]
    public void AReasonThatIsOnlyLongEnoughWithItsSpaces_IsInvalid() =>
        Assert.False(_validator.Validate(new EscalateTicketRequest("  abc   ", "Team Lead")).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingActor_IsInvalid(string escalatedBy)
    {
        var result = _validator.Validate(new EscalateTicketRequest("Customer has chased twice", escalatedBy));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(EscalateTicketRequest.EscalatedBy));
    }
}

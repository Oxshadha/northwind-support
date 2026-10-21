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
        Assert.True(_validator.Validate(new EscalateTicketRequest(new string('x', length))).IsValid);

    [Theory]
    [InlineData(4)]
    [InlineData(501)]
    public void AReasonOutsideTheLimits_IsInvalid(int length) =>
        Assert.False(_validator.Validate(new EscalateTicketRequest(new string('x', length))).IsValid);

    [Fact]
    public void AReasonThatIsOnlyLongEnoughWithItsSpaces_IsInvalid() =>
        Assert.False(_validator.Validate(new EscalateTicketRequest("  abc   ")).IsValid);
}

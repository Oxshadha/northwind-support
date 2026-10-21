using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Auth.Commands.Login;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Auth;

public class LoginCommandHandlerTests
{
    private const string Email = "sara.lindqvist@northwind-support.example";
    private const string Password = "correct horse battery";

    private readonly Mock<IAgentRepository> _agents = new();
    private readonly FakePasswordHasher _passwords = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _tokens.Setup(t => t.Issue(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new IssuedToken("signed.jwt.token", FixedClock.DefaultNow.AddHours(8)));

        _handler = new LoginCommandHandler(
            _agents.Object, _passwords, _tokens.Object, _unitOfWork.Object, NullLogger<LoginCommandHandler>.Instance);
    }

    private Agent ExistingAgent(bool isActive = true, bool hasPassword = true)
    {
        var agent = new Agent("Sara Lindqvist", Email, 6, FixedClock.DefaultNow).WithId(4);

        if (hasPassword)
        {
            agent.SetPasswordHash(_passwords.Hash(Password));
        }

        if (!isActive)
        {
            agent.Deactivate();
        }

        _agents.Setup(a => a.GetByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
        return agent;
    }

    private Task<LoginResponse> SignIn(string email = Email, string password = Password) =>
        _handler.HandleAsync(new LoginRequest(email, password), CancellationToken.None);

    [Fact]
    public async Task CorrectCredentials_ReturnATokenItsExpiryAndTheAgent()
    {
        ExistingAgent();

        var response = await SignIn();

        Assert.Equal("signed.jwt.token", response.Token);
        Assert.Equal(FixedClock.DefaultNow.AddHours(8), response.ExpiresAtUtc);
        Assert.Equal(new CurrentAgentDto(4, "Sara Lindqvist", Email), response.Agent);
    }

    [Fact]
    public async Task TheEmail_IsTrimmed()
    {
        ExistingAgent();

        var response = await SignIn(email: $"  {Email} ");

        Assert.Equal(4, response.Agent.Id);
    }

    [Fact]
    public async Task AWrongPassword_IsRefused_WithoutAToken()
    {
        ExistingAgent();

        var error = await Assert.ThrowsAsync<UnauthorizedException>(() => SignIn(password: "wrong"));

        Assert.Equal(LoginCommandHandler.FailureMessage, error.Message);
        _tokens.Verify(t => t.Issue(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
    }

    [Fact]
    public async Task AnUnknownEmail_GetsExactlyTheSameRefusal_AsAWrongPassword()
    {
        ExistingAgent();
        var wrongPassword = await Assert.ThrowsAsync<UnauthorizedException>(() => SignIn(password: "wrong"));

        var unknownEmail = await Assert.ThrowsAsync<UnauthorizedException>(() => SignIn(email: "nobody@example.com"));

        Assert.Equal(wrongPassword.Message, unknownEmail.Message);
    }

    [Fact]
    public async Task AnInactiveAgent_CannotSignIn_EvenWithTheRightPassword()
    {
        ExistingAgent(isActive: false);

        var error = await Assert.ThrowsAsync<UnauthorizedException>(() => SignIn());

        Assert.Equal(LoginCommandHandler.FailureMessage, error.Message);
    }

    [Fact]
    public async Task AnAgentWithoutCredentials_CannotSignIn()
    {
        ExistingAgent(hasPassword: false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => SignIn());
    }

    [Theory]
    [InlineData("unknown email")]
    [InlineData("no credentials")]
    [InlineData("wrong password")]
    [InlineData("inactive")]
    [InlineData("success")]
    public async Task EveryPath_DoesExactlyOnePasswordCheck_SoTimingRevealsNothing(string path)
    {
        if (path != "unknown email")
        {
            ExistingAgent(isActive: path != "inactive", hasPassword: path != "no credentials");
        }

        var password = path == "wrong password" ? "wrong" : Password;

        try
        {
            await SignIn(password: password);
        }
        catch (UnauthorizedException)
        {
            // The refusal itself is covered above; this test is about the work done.
        }

        Assert.Single(_passwords.CheckedHashes);
    }

    [Fact]
    public async Task AnUnknownEmail_IsCheckedAgainstTheDummyHash()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => SignIn(email: "nobody@example.com"));

        Assert.Equal(_passwords.DummyHash, Assert.Single(_passwords.CheckedHashes));
    }

    [Fact]
    public async Task AnOutdatedHash_IsUpgradedOnSuccessfulSignIn()
    {
        var agent = ExistingAgent();
        _passwords.ReportRehashNeeded = true;

        await SignIn();

        Assert.Equal(_passwords.Hash(Password), agent.PasswordHash);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }
}

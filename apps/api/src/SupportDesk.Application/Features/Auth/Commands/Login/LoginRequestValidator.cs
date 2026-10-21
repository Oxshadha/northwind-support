using FluentValidation;
using SupportDesk.Application.Contracts.Auth;

namespace SupportDesk.Application.Features.Auth.Commands.Login;

/// <summary>
/// Rejects requests that cannot be a sign-in at all. Says nothing about whether an account
/// exists: that is only ever answered with the same 401.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();

        // Long enough for any real password; short enough that hashing it cannot be abused.
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

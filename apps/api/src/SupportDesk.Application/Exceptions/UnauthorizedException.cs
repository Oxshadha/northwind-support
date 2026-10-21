namespace SupportDesk.Application.Exceptions;

/// <summary>
/// Thrown when a caller cannot be identified, such as a failed sign-in. Surfaces as HTTP 401.
/// </summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}

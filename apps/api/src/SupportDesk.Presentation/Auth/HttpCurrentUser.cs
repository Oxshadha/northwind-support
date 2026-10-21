using System.Globalization;
using Microsoft.IdentityModel.JsonWebTokens;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Presentation.Auth;

/// <summary>
/// The signed-in agent, read from the claims of the token this request was authenticated with.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int AgentId =>
        int.Parse(Claim(JwtRegisteredClaimNames.Sub), NumberStyles.None, CultureInfo.InvariantCulture);

    public string FullName => Claim(JwtRegisteredClaimNames.Name);

    private string Claim(string type) =>
        accessor.HttpContext?.User.FindFirst(type)?.Value
        ?? throw new InvalidOperationException(
            $"The request has no '{type}' claim. Is the endpoint missing the authentication requirement?");
}

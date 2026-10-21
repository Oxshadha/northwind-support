namespace SupportDesk.Application.Abstractions;

/// <summary>
/// The signed-in agent making this request, as their token identifies them. Every endpoint
/// except sign-in requires a valid token, so handlers can rely on one being present.
/// </summary>
public interface ICurrentUser
{
    int AgentId { get; }

    string FullName { get; }
}

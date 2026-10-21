using SupportDesk.Application.Abstractions;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>The signed-in agent, as a verified token would name them.</summary>
public sealed class FakeCurrentUser(int agentId = 4, string fullName = "Sara Lindqvist") : ICurrentUser
{
    public int AgentId { get; } = agentId;

    public string FullName { get; } = fullName;
}

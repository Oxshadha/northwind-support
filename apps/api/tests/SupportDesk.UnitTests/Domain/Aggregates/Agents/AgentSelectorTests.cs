using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Agents;

/// <summary>Assignment, against the seeded team: who is busy, who is full, who is inactive.</summary>
public class AgentSelectorTests
{
    private static readonly AgentWorkload Alex = new(1, "Alex Turner", true, 10, 5, [1, 3]);
    private static readonly AgentWorkload Ben = new(2, "Ben Osei", true, 8, 3, [3, 4]);
    private static readonly AgentWorkload Priya = new(3, "Priya Nair", true, 8, 4, [4, 5]);
    private static readonly AgentWorkload Sara = new(4, "Sara Lindqvist", true, 6, 2, [1, 2]);
    private static readonly AgentWorkload Marcus = new(5, "Marcus Doyle", true, 6, 6, [2, 5]);
    private static readonly AgentWorkload Yuki = new(6, "Yuki Tanaka", false, 8, 0, [3]);

    private static readonly AgentWorkload[] Team = [Alex, Ben, Priya, Sara, Marcus, Yuki];

    [Fact]
    public void Select_ChoosesTheFewestOpenTickets() =>
        // Technical needs no specialist: Sara (2) beats Ben (3); Yuki has 0 but is inactive.
        Assert.Equal(Sara.AgentId, AgentSelector.Select(Team, Policies.Technical()).AgentId);

    [Fact]
    public void Select_OnlyConsidersSpecialists_WhenTheCategoryRequiresOne() =>
        // Security: Priya and Marcus; Marcus is full.
        Assert.Equal(Priya.AgentId, AgentSelector.Select(Team, Policies.Security()).AgentId);

    [Fact]
    public void Select_SkipsAnAgentAtTheirLimit()
    {
        var decision = AgentSelector.Select([Marcus], Policies.Billing());

        Assert.Null(decision.AgentId);
    }

    [Fact]
    public void Select_AcceptsAnAgentOneBelowTheirLimit()
    {
        var almostFull = Marcus with { OpenTicketCount = 5 };

        Assert.Equal(Marcus.AgentId, AgentSelector.Select([almostFull], Policies.Billing()).AgentId);
    }

    [Fact]
    public void Select_SkipsInactiveAgents()
    {
        var decision = AgentSelector.Select([Yuki], Policies.Technical());

        Assert.Null(decision.AgentId);
    }

    [Fact]
    public void Select_BreaksATieOnOpenTickets_ByMostSpareCapacity()
    {
        var small = new AgentWorkload(1, "Small", true, 6, 2, []);
        var large = new AgentWorkload(2, "Large", true, 10, 2, []);

        Assert.Equal(large.AgentId, AgentSelector.Select([small, large], Policies.General()).AgentId);
    }

    [Fact]
    public void Select_BreaksAFullTie_ByLowestId_WhateverTheInputOrder()
    {
        var first = new AgentWorkload(7, "First", true, 8, 2, []);
        var second = new AgentWorkload(9, "Second", true, 8, 2, []);

        Assert.Equal(7, AgentSelector.Select([second, first], Policies.General()).AgentId);
        Assert.Equal(7, AgentSelector.Select([first, second], Policies.General()).AgentId);
    }

    [Fact]
    public void Select_WithNobodyEligible_IsUnassigned_WithAReason()
    {
        var decision = AgentSelector.Select([Marcus, Yuki], Policies.Billing());

        Assert.Null(decision.AgentId);
        Assert.Contains("no active Billing specialist", decision.Reason);
    }

    [Fact]
    public void Reevaluate_KeepsAnOwnerAtTheirLimit_BecauseTheTicketIsAlreadyTheirs()
    {
        var decision = AgentSelector.Reevaluate(Marcus.AgentId, Team, Policies.Billing());

        Assert.Equal(Marcus.AgentId, decision.AgentId);
        Assert.Contains("Kept by Marcus Doyle", decision.Reason);
    }

    [Fact]
    public void Reevaluate_ReassignsAwayFromAnInactiveOwner()
    {
        var decision = AgentSelector.Reevaluate(Yuki.AgentId, Team, Policies.Technical());

        Assert.Equal(Sara.AgentId, decision.AgentId);
        Assert.Contains("Yuki Tanaka is no longer active", decision.Reason);
    }

    [Fact]
    public void Reevaluate_ReassignsAwayFromAnOwnerWhoIsNotASpecialist()
    {
        // A team lead may assign by hand outside the specialist rule; escalation re-applies it.
        var decision = AgentSelector.Reevaluate(Alex.AgentId, Team, Policies.Billing());

        Assert.Equal(Sara.AgentId, decision.AgentId);
        Assert.Contains("Alex Turner is not a Billing specialist", decision.Reason);
    }

    [Fact]
    public void Reevaluate_WithNoOwner_SelectsOne() =>
        Assert.Equal(Sara.AgentId, AgentSelector.Reevaluate(null, Team, Policies.Billing()).AgentId);
}

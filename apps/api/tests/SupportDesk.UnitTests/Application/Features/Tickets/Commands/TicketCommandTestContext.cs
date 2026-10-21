using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.AssignTicket;
using SupportDesk.Application.Features.Tickets.Commands.ChangeTicketStatus;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

/// <summary>
/// Shared arrangement for the ticket command tests: mocked repositories and queries, a fixed
/// clock, the development SLA rules, the seeded team's workloads, and command handlers wired
/// to all of them.
/// </summary>
internal sealed class TicketCommandTestContext
{
    /// <summary>The seeded team, as assignment sees it (ids, limits, open counts, specializations).</summary>
    public static readonly AgentWorkload[] SeededTeam =
    [
        new(1, "Alex Turner", true, 10, 5, [1, 3]),
        new(2, "Ben Osei", true, 8, 3, [3, 4]),
        new(3, "Priya Nair", true, 8, 4, [4, 5]),
        new(4, "Sara Lindqvist", true, 6, 2, [1, 2]),
        new(5, "Marcus Doyle", true, 6, 6, [2, 5]),
        new(6, "Yuki Tanaka", false, 8, 0, [3])
    ];

    public TicketCommandTestContext()
    {
        WithCustomer(CustomerTier.Standard);
        WithCategory(Policies.Technical());
        WithWorkloads(SeededTeam);

        Tickets.Setup(t => t.NextReferenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("TCK-0041");

        Tickets.Setup(t => t.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, CancellationToken>((ticket, _) => AddedTicket = ticket)
            .Returns(Task.CompletedTask);

        // Every command returns the ticket as re-read once its change has been saved.
        TicketQueries.Setup(q => q.GetDetailAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketDetailDto
            {
                Id = 41,
                Reference = "TCK-0041",
                Customer = new CustomerContactDto(1, "Contoso Ltd", "support@contoso.example", null, CustomerTier.Premium),
                Category = new CategoryDto(1, "General", false)
            });

        // The escalation just written is the newest history row. Its id is 0 here because
        // nothing is saved to a database; the handler looks it up by that same id.
        TicketQueries.Setup(q => q.GetEscalationsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TicketEscalationDto { Id = 0, Reason = "recorded" }]);

        var getTicket = new GetTicketQueryHandler(TicketQueries.Object);

        RaiseTicket = new RaiseTicketCommandHandler(
            Tickets.Object, Customers.Object, Categories.Object, Agents.Object, UnitOfWork.Object,
            AssignmentLock, Sla, Clock, getTicket, NullLogger<RaiseTicketCommandHandler>.Instance);

        EscalateTicket = new EscalateTicketCommandHandler(
            Tickets.Object, Customers.Object, Categories.Object, Agents.Object, UnitOfWork.Object,
            AssignmentLock, Sla, Clock, getTicket, TicketQueries.Object,
            NullLogger<EscalateTicketCommandHandler>.Instance);

        ChangeTicketStatus = new ChangeTicketStatusCommandHandler(Tickets.Object, UnitOfWork.Object, Clock, getTicket);

        AssignTicket = new AssignTicketCommandHandler(
            Tickets.Object, Agents.Object, UnitOfWork.Object, Clock, getTicket,
            NullLogger<AssignTicketCommandHandler>.Instance);
    }

    public Mock<ITicketRepository> Tickets { get; } = new();

    public Mock<ICustomerRepository> Customers { get; } = new();

    public Mock<ICategoryRepository> Categories { get; } = new();

    public Mock<IAgentRepository> Agents { get; } = new();

    public Mock<IUnitOfWork> UnitOfWork { get; } = new();

    public Mock<ITicketQueries> TicketQueries { get; } = new();

    public PassThroughAssignmentLock AssignmentLock { get; } = new();

    public SlaPolicy Sla { get; } = Policies.DefaultSla();

    public FixedClock Clock { get; } = new();

    public RaiseTicketCommandHandler RaiseTicket { get; }

    public EscalateTicketCommandHandler EscalateTicket { get; }

    public ChangeTicketStatusCommandHandler ChangeTicketStatus { get; }

    public AssignTicketCommandHandler AssignTicket { get; }

    /// <summary>The ticket handed to the repository by the last create call.</summary>
    public Ticket? AddedTicket { get; private set; }

    /// <summary>Makes every customer lookup return a customer of this tier.</summary>
    public void WithCustomer(CustomerTier tier) =>
        Customers.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer("Contoso Ltd", "support@contoso.example", null, tier, Clock.UtcNow).WithId(1));

    /// <summary>Makes every category lookup return this category.</summary>
    public void WithCategory(Category category) =>
        Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

    public void WithWorkloads(params AgentWorkload[] workloads) =>
        Agents.Setup(a => a.GetWorkloadsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(workloads);

    /// <summary>Makes <see cref="ITicketRepository.GetByIdAsync"/> return this ticket.</summary>
    public Ticket ExistingTicket(Ticket ticket)
    {
        Tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        return ticket;
    }

    public void ExistingAgent(int id, string fullName = "Alex Turner", bool isActive = true)
    {
        var agent = new Agent(fullName, $"agent{id}@example.com", maxOpenTickets: 10, Clock.UtcNow);

        if (!isActive)
        {
            agent.Deactivate();
        }

        Agents.Setup(a => a.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
    }

    public void VerifySaved(Times times) =>
        UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), times);
}

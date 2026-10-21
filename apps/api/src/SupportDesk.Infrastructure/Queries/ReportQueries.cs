using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Reports;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Reports, each computed by the database as one aggregate query: the tickets are never
/// loaded into memory, however many there are.
/// </summary>
public sealed class ReportQueries(SupportDbContext db, IClock clock, SlaPolicy sla) : IReportQueries
{
    /// <remarks>
    /// One statement: the tickets in the period are classified with a CASE built from the same
    /// SLA predicates the list filter uses, grouped, counted per state with COUNT(CASE ...), and
    /// (per agent) joined to the agent's name.
    /// </remarks>
    public async Task<IReadOnlyList<SlaSummaryGroupDto>> GetSlaSummaryAsync(
        DateTime fromUtc,
        DateTime toUtc,
        SlaSummaryGrouping groupBy,
        CancellationToken ct)
    {
        var rows = db.Set<Ticket>()
            .AsNoTracking()
            .Where(t => t.CreatedAtUtc >= fromUtc && t.CreatedAtUtc < toUtc)
            .Select(ToSlaRow(clock.UtcNow, sla.AtRiskThresholdPercent));

        return groupBy switch
        {
            SlaSummaryGrouping.Priority => await ByPriorityAsync(rows, ct),
            SlaSummaryGrouping.Agent => await ByAgentAsync(rows, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, "Unknown grouping.")
        };
    }

    /// <remarks>
    /// Every priority is listed, including those with no tickets in the period: a report that
    /// silently drops a row reads as "no data" rather than "nothing went wrong".
    /// </remarks>
    private static async Task<IReadOnlyList<SlaSummaryGroupDto>> ByPriorityAsync(IQueryable<SlaRow> rows, CancellationToken ct)
    {
        var totals = await Totals(rows.GroupBy(r => r.Priority)).ToListAsync(ct);

        // Filling the known priorities is not aggregation: the counting stays one SQL statement.
        return Enum.GetValues<TicketPriority>()
            .OrderDescending()
            .Select(priority => totals.FirstOrDefault(t => t.Key == priority) is { } found
                ? ToDto(found, priority.ToString(), agentId: null, priority)
                : Empty(priority.ToString(), agentId: null, priority))
            .ToList();
    }

    /// <remarks>
    /// Starts from the agents, so an active agent with no tickets in the period is listed with
    /// zeros: exactly who a team lead looks for when rebalancing work. Inactive agents appear
    /// only while they still have tickets in the period. Tickets nobody owns form an
    /// "Unassigned" group, joined in the same statement with UNION ALL.
    /// </remarks>
    private async Task<IReadOnlyList<SlaSummaryGroupDto>> ByAgentAsync(IQueryable<SlaRow> rows, CancellationToken ct)
    {
        var totals = Totals(rows.GroupBy(r => r.AgentId));

        // A matched row's Key is the agent's id, so "Key is null" means no tickets: EF cannot
        // test the joined row itself for null, but it can test a column (and COALESCE the counts).
        var agents =
            from agent in db.Set<Agent>()
            join total in totals on (int?)agent.Id equals total.Key into matched
            from total in matched.DefaultIfEmpty()
            where agent.IsActive || total!.Key != null
            select new AgentGroupRow
            {
                AgentId = agent.Id,
                AgentName = agent.FullName,
                Total = (int?)total!.Total ?? 0,
                WithinSla = (int?)total!.WithinSla ?? 0,
                AtRisk = (int?)total!.AtRisk ?? 0,
                Breached = (int?)total!.Breached ?? 0,
                Met = (int?)total!.Met ?? 0,
                NoSla = (int?)total!.NoSla ?? 0,
                AverageResolutionSeconds = total!.AverageResolutionSeconds
            };

        var unassigned =
            from total in totals
            where total.Key == null
            select new AgentGroupRow
            {
                AgentId = null,
                AgentName = null,
                Total = total.Total,
                WithinSla = total.WithinSla,
                AtRisk = total.AtRisk,
                Breached = total.Breached,
                Met = total.Met,
                NoSla = total.NoSla,
                AverageResolutionSeconds = total.AverageResolutionSeconds
            };

        var groups = await agents.Concat(unassigned).ToListAsync(ct);

        var dtos = groups
            .Select(g => ToDto(
                new GroupTotals<int?>
                {
                    Key = g.AgentId,
                    Total = g.Total,
                    WithinSla = g.WithinSla,
                    AtRisk = g.AtRisk,
                    Breached = g.Breached,
                    Met = g.Met,
                    NoSla = g.NoSla,
                    AverageResolutionSeconds = g.AverageResolutionSeconds
                },
                g.AgentName ?? "Unassigned",
                g.AgentId,
                priority: null))
            .OrderBy(dto => dto.AgentId is null)
            .ThenBy(dto => dto.Key, StringComparer.Ordinal)
            .ToList();

        // Like the priorities, "Unassigned" is always listed, with zeros when everything had an owner.
        if (dtos.All(dto => dto.AgentId is not null))
        {
            dtos.Add(Empty("Unassigned", agentId: null, priority: null));
        }

        return dtos;
    }

    /// <summary>Per group: how many tickets, how many in each SLA state, and the mean resolution time.</summary>
    private static IQueryable<GroupTotals<TKey>> Totals<TKey>(IQueryable<IGrouping<TKey, SlaRow>> groups) =>
        groups.Select(g => new GroupTotals<TKey>
        {
            Key = g.Key,
            Total = g.Count(),
            WithinSla = g.Count(r => r.Status == SlaStatus.WithinSla),
            AtRisk = g.Count(r => r.Status == SlaStatus.AtRisk),
            Breached = g.Count(r => r.Status == SlaStatus.Breached),
            Met = g.Count(r => r.Status == SlaStatus.Met),
            NoSla = g.Count(r => r.Status == SlaStatus.NotApplicable),
            // Cast first: AVG over an int column would round down in SQL Server.
            AverageResolutionSeconds = g.Average(r => (double?)r.ResolutionSeconds)
        });

    /// <summary>
    /// What the report needs from one ticket: its group keys, its SLA state (as a SQL CASE)
    /// and, when resolved, how many minutes it took.
    /// </summary>
    private static Expression<Func<Ticket, SlaRow>> ToSlaRow(DateTime now, int atRiskThresholdPercent)
    {
        var status = SlaSql.StatusOf(now, atRiskThresholdPercent);
        var ticket = status.Parameters[0];

        // Seconds, not minutes: DATEDIFF counts boundaries crossed, so a minute-level count can be
        // a whole minute out. NULL for an unresolved ticket, which AVG then ignores.
        Expression<Func<Ticket, int?>> resolutionSeconds = t => EF.Functions.DateDiffSecond(t.CreatedAtUtc, t.ResolvedAtUtc);

        var body = Expression.MemberInit(
            Expression.New(typeof(SlaRow)),
            Expression.Bind(typeof(SlaRow).GetProperty(nameof(SlaRow.AgentId))!, Expression.Property(ticket, nameof(Ticket.AssignedAgentId))),
            Expression.Bind(typeof(SlaRow).GetProperty(nameof(SlaRow.Priority))!, Expression.Property(ticket, nameof(Ticket.Priority))),
            Expression.Bind(typeof(SlaRow).GetProperty(nameof(SlaRow.Status))!, status.Body),
            Expression.Bind(
                typeof(SlaRow).GetProperty(nameof(SlaRow.ResolutionSeconds))!,
                new ReplaceParameter(resolutionSeconds.Parameters[0], ticket).Visit(resolutionSeconds.Body)));

        return Expression.Lambda<Func<Ticket, SlaRow>>(body, ticket);
    }

    private static SlaSummaryGroupDto ToDto<TKey>(GroupTotals<TKey> totals, string key, int? agentId, TicketPriority? priority) => new()
    {
        Key = key,
        AgentId = agentId,
        Priority = priority,
        Total = totals.Total,
        WithinSla = totals.WithinSla,
        AtRisk = totals.AtRisk,
        Breached = totals.Breached,
        Met = totals.Met,
        NoSla = totals.NoSla,
        AverageResolutionHours = totals.AverageResolutionSeconds is { } seconds
            ? Math.Round(seconds / 3600, 1, MidpointRounding.AwayFromZero)
            : null
    };

    private static SlaSummaryGroupDto Empty(string key, int? agentId, TicketPriority? priority) => new()
    {
        Key = key,
        AgentId = agentId,
        Priority = priority
    };

    private sealed class AgentGroupRow
    {
        public int? AgentId { get; init; }

        public string? AgentName { get; init; }

        public int Total { get; init; }

        public int WithinSla { get; init; }

        public int AtRisk { get; init; }

        public int Breached { get; init; }

        public int Met { get; init; }

        public int NoSla { get; init; }

        public double? AverageResolutionSeconds { get; init; }
    }

    private sealed class SlaRow
    {
        public int? AgentId { get; init; }

        public TicketPriority Priority { get; init; }

        public SlaStatus Status { get; init; }

        public int? ResolutionSeconds { get; init; }
    }

    private sealed class GroupTotals<TKey>
    {
        public required TKey Key { get; init; }

        public int Total { get; init; }

        public int WithinSla { get; init; }

        public int AtRisk { get; init; }

        public int Breached { get; init; }

        public int Met { get; init; }

        public int NoSla { get; init; }

        public double? AverageResolutionSeconds { get; init; }
    }

    private sealed class ReplaceParameter(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}

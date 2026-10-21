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

    private static async Task<IReadOnlyList<SlaSummaryGroupDto>> ByPriorityAsync(IQueryable<SlaRow> rows, CancellationToken ct)
    {
        var totals = await Totals(rows.GroupBy(r => r.Priority))
            .OrderByDescending(t => t.Key)
            .ToListAsync(ct);

        return totals.Select(t => ToDto(t, t.Key.ToString(), agentId: null, priority: t.Key)).ToList();
    }

    private async Task<IReadOnlyList<SlaSummaryGroupDto>> ByAgentAsync(IQueryable<SlaRow> rows, CancellationToken ct)
    {
        var totals = await (
                from total in Totals(rows.GroupBy(r => r.AgentId))
                from agent in db.Set<Agent>().Where(a => a.Id == total.Key).DefaultIfEmpty()
                select new { Total = total, AgentName = agent == null ? null : agent.FullName })
            .ToListAsync(ct);

        return totals
            .Select(t => ToDto(t.Total, t.AgentName ?? "Unassigned", t.Total.Key, priority: null))
            .OrderBy(dto => dto.AgentId is null)
            .ThenBy(dto => dto.Key, StringComparer.Ordinal)
            .ToList();
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

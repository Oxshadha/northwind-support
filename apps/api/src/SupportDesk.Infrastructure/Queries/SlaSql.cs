using System.Linq.Expressions;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// <see cref="SlaEvaluator.Evaluate"/> written as SQL-translatable expressions: the one
/// definition of each SLA state the database uses, shared by the list filter and the report.
/// </summary>
/// <remarks>
/// The states are disjoint and together cover every ticket, matching the domain rule; a
/// parity test checks the two agree on the seed data. "At risk from" is the due time minus the
/// threshold share of the window, which keeps the arithmetic on one column (DATEADD) and the
/// comparison with "now" a plain parameter.
/// </remarks>
internal static class SlaSql
{
    /// <summary>Tickets in this state at <paramref name="now"/>.</summary>
    public static Expression<Func<Ticket, bool>> Is(SlaStatus status, DateTime now, int percent) =>
        status switch
        {
            SlaStatus.NotApplicable => t => t.DueAtUtc == null,

            SlaStatus.Met => t => t.ResolvedAtUtc != null && t.ResolvedAtUtc <= t.DueAtUtc,

            SlaStatus.Breached => t =>
                t.DueAtUtc != null &&
                (t.ResolvedAtUtc != null ? t.ResolvedAtUtc > t.DueAtUtc : t.DueAtUtc < now),

            SlaStatus.AtRisk => t =>
                t.ResolvedAtUtc == null && t.DueAtUtc >= now &&
                t.SlaWindowSeconds != null &&
                t.DueAtUtc!.Value.AddSeconds(-(t.SlaWindowSeconds!.Value * percent / 100.0)) <= now,

            SlaStatus.WithinSla => t =>
                t.ResolvedAtUtc == null && t.DueAtUtc >= now &&
                (t.SlaWindowSeconds == null ||
                 t.DueAtUtc!.Value.AddSeconds(-(t.SlaWindowSeconds!.Value * percent / 100.0)) > now),

            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown SLA status.")
        };

    /// <summary>
    /// Each ticket's state as a single SQL CASE, built from <see cref="Is"/> so the report
    /// classifies tickets exactly as the filter selects them.
    /// </summary>
    public static Expression<Func<Ticket, SlaStatus>> StatusOf(DateTime now, int percent)
    {
        var ticket = Expression.Parameter(typeof(Ticket), "t");

        // CASE WHEN NotApplicable THEN ... WHEN Met ... WHEN Breached ... WHEN AtRisk ... ELSE WithinSla.
        Expression body = Expression.Constant(SlaStatus.WithinSla);

        foreach (var status in new[] { SlaStatus.AtRisk, SlaStatus.Breached, SlaStatus.Met, SlaStatus.NotApplicable })
        {
            body = Expression.Condition(
                Rebind(Is(status, now, percent), ticket),
                Expression.Constant(status),
                body);
        }

        return Expression.Lambda<Func<Ticket, SlaStatus>>(body, ticket);
    }

    /// <summary>A ticket-level predicate applied to the ticket inside a joined row, e.g. x => pred(x.Ticket).</summary>
    public static Expression<Func<TRow, bool>> On<TRow>(
        Expression<Func<Ticket, bool>> predicate,
        Expression<Func<TRow, Ticket>> ticketOf) =>
        Expression.Lambda<Func<TRow, bool>>(Rebind(predicate, ticketOf.Body), ticketOf.Parameters);

    private static Expression Rebind(LambdaExpression lambda, Expression ticket) =>
        new ReplaceParameter(lambda.Parameters[0], ticket).Visit(lambda.Body);

    private sealed class ReplaceParameter(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}

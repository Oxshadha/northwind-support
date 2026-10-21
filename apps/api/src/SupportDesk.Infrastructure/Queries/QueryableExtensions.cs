using System.Linq.Expressions;

namespace SupportDesk.Infrastructure.Queries;

internal static class QueryableExtensions
{
    /// <summary>
    /// Adds the predicate only when the filter was supplied, so an optional filter reads as one
    /// line and still translates to SQL.
    /// </summary>
    public static IQueryable<T> WhereIf<T>(
        this IQueryable<T> source,
        bool condition,
        Expression<Func<T, bool>> predicate) =>
        condition ? source.Where(predicate) : source;
}

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Runs work that reads agents' workloads and then assigns a ticket as one exclusive,
/// all-or-nothing step.
/// </summary>
/// <remarks>
/// Without it, two tickets raised at the same moment could both see an agent with one free
/// slot and both be given to them, pushing the agent past their limit.
/// </remarks>
public interface IAssignmentLock
{
    /// <summary>
    /// Runs <paramref name="operation"/> while no other assignment runs, and commits what it
    /// saves only if it completes. The operation may be run again after a transient database
    /// failure, so it must load what it changes itself.
    /// </summary>
    Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct);
}

using SupportDesk.Application.Abstractions;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>
/// Runs the operation straight away, and counts how often, so a handler test can check its
/// assignment work went through the lock.
/// </summary>
public sealed class PassThroughAssignmentLock : IAssignmentLock
{
    public int Runs { get; private set; }

    public Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        Runs++;
        return operation(ct);
    }
}

using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Infrastructure.Data;

/// <summary>
/// Serialises assignment with a SQL Server application lock held for one transaction.
/// </summary>
/// <remarks>
/// An application lock guards the whole read-decide-write step without locking any table, so
/// list and detail reads are never blocked. Row locks would not do: the rows that decide an
/// agent's load (their open tickets) are not the row being written. The lock is released with
/// the transaction, so it cannot leak.
/// <para>
/// The context retries transient failures, which only works when the retry repeats the whole
/// transaction, so the operation runs inside the execution strategy and starts each attempt
/// from a clean change tracker.
/// </para>
/// </remarks>
internal sealed class SqlServerAssignmentLock(SupportDbContext db) : IAssignmentLock
{
    private const string AcquireLock = """
        DECLARE @result int;
        EXEC @result = sp_getapplock
            @Resource = N'SupportDesk.TicketAssignment',
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 10000;
        IF @result < 0
            THROW 50001, 'Timed out waiting to assign a ticket; please try again.', 1;
        """;

    public Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(
            operation,
            async (_, work, token) =>
            {
                // A retried attempt must not carry the half-applied changes of the failed one.
                db.ChangeTracker.Clear();

                await using var transaction = await db.Database.BeginTransactionAsync(token);
                await db.Database.ExecuteSqlRawAsync(AcquireLock, token);

                var result = await work(token);

                await transaction.CommitAsync(token);
                return result;
            },
            verifySucceeded: null,
            ct);
}

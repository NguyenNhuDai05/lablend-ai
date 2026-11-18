using Borrowing.Application;
using Borrowing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Borrowing.Infrastructure;

public sealed class LoanReconciliation(BorrowingDbContext db, LoanLifecycle lifecycle, IncidentWorkflow incidents, TimeProvider clock,
    IOptions<BorrowingWorkflowOptions> options, ILogger<LoanReconciliation> logger)
{
    public async Task Run(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var work = await db.InventoryOperations.AsNoTracking()
            .Where(o => (o.State == "PENDING" || o.State == "RETRY" || o.State == "COMPENSATING") && o.NextRetryAt <= now)
            .OrderBy(o => o.NextRetryAt).ThenBy(o => o.OperationId).Take(options.Value.BatchSize).Select(o => new { o.LoanId, o.OperationId }).ToListAsync(ct);
        foreach (var record in work)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var gate = await LoanLock.TryAcquire(db, record.LoanId, ct);
                if (gate == null) continue;
                db.ChangeTracker.Clear();
                var operation = await db.InventoryOperations.SingleAsync(o => o.OperationId == record.OperationId, ct);
                if (operation.State is not ("PENDING" or "RETRY" or "COMPENSATING") || operation.NextRetryAt > clock.GetUtcNow()) continue;
                if (operation.OperationType.StartsWith("INC_", StringComparison.Ordinal)) await incidents.Apply(operation, ct);
                else await lifecycle.Apply(operation, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { logger.LogWarning("Reconciliation deferred loan {LoanId}; errorType={ErrorType}", record.LoanId, e.GetType().Name); }
            finally { db.ChangeTracker.Clear(); }
        }
        var expiry = now.AddHours(-options.Value.PickupHours);
        var expired = await db.LoanRequests.AsNoTracking().Where(l => l.Status == LoanStatus.APPROVED && (l.ReviewedAt <= expiry || l.DueAt <= now)
            && !db.InventoryOperations.Any(o => o.LoanId == l.Id && o.State != "COMPLETED" && o.State != "FAILED"))
            .OrderBy(l => l.ReviewedAt).ThenBy(l => l.Id).Take(options.Value.BatchSize).Select(l => l.Id).ToListAsync(ct);
        foreach (var loanId in expired)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var gate = await LoanLock.TryAcquire(db, loanId, ct);
                if (gate == null) continue;
                db.ChangeTracker.Clear();
                var loan = await db.LoanRequests.SingleAsync(l => l.Id == loanId, ct);
                if (loan.Status != LoanStatus.APPROVED || loan.ReviewedAt > expiry && loan.DueAt > now
                    || await db.InventoryOperations.AnyAsync(o => o.LoanId == loanId && o.State != "COMPLETED" && o.State != "FAILED", ct)) continue;
                var operation = new InventoryOperation { LoanId = loanId, OperationType = "RELEASE", StartedAt = now, NextRetryAt = now,
                    CorrelationId = "pickup-expiry-" + loanId.ToString("N") };
                db.InventoryOperations.Add(operation); await db.SaveChangesAsync(ct);
                await lifecycle.Apply(operation, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { logger.LogWarning("Reservation expiry deferred loan {LoanId}; errorType={ErrorType}", loanId, e.GetType().Name); }
            finally { db.ChangeTracker.Clear(); }
        }
        logger.LogInformation("Reservation reconciliation scanned {Operations} operations and {Expirations} expirations", work.Count, expired.Count);
    }
}

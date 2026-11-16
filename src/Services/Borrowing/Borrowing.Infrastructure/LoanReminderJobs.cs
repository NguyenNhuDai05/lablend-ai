using Borrowing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Borrowing.Infrastructure;

public sealed class ReminderOptions
{
    public int DueSoonMinutes { get; set; } = 1440;
    public int PollSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 20;
}

public sealed class LoanReminderJobs(BorrowingDbContext db, TimeProvider clock, IOptions<ReminderOptions> options, ILogger<LoanReminderJobs> logger)
{
    public async Task<int> Run(bool overdue, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var type = overdue ? "LoanOverdue" : "LoanDueSoon";
        var threshold = overdue ? 0 : options.Value.DueSoonMinutes; var until = now.AddMinutes(threshold);
        var candidates = await db.LoanRequests.AsNoTracking().Where(l => l.Status == LoanStatus.BORROWED && l.Items.Any(i => i.ItemStatus == ItemStatus.BORROWED)
            && !db.InventoryOperations.Any(o => o.LoanId == l.Id && o.State != "COMPLETED" && o.State != "FAILED")
            && (overdue ? l.DueAt < now : l.DueAt > now && l.DueAt <= until)
            && !db.ReminderDispatches.Any(d => d.LoanId == l.Id && d.EventType == type && d.DueAt == l.DueAt && d.ThresholdMinutes == threshold))
            .OrderBy(l => l.DueAt).ThenBy(l => l.Id).Take(options.Value.BatchSize).Select(l => l.Id).ToListAsync(ct);
        var dispatched = 0;
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var gate = await LoanLock.TryAcquire(db, id, ct);
                if (gate == null) continue;
                db.ChangeTracker.Clear();
                var loan = await db.LoanRequests.Include(l => l.Items).SingleAsync(l => l.Id == id, ct);
                now = clock.GetUtcNow();
                if (loan.Status != LoanStatus.BORROWED || !loan.Items.Any(i => i.ItemStatus == ItemStatus.BORROWED)
                    || await db.InventoryOperations.AnyAsync(o => o.LoanId == id && o.State != "COMPLETED" && o.State != "FAILED", ct)
                    || (overdue ? loan.DueAt >= now : loan.DueAt <= now || loan.DueAt > now.AddMinutes(threshold))
                    || await db.ReminderDispatches.AnyAsync(d => d.LoanId == id && d.EventType == type && d.DueAt == loan.DueAt && d.ThresholdMinutes == threshold, ct)) continue;
                var dispatch = new ReminderDispatch { LoanId = id, EventType = type, DueAt = loan.DueAt, ThresholdMinutes = threshold, CreatedAt = now };
                db.ReminderDispatches.Add(dispatch);
                db.OutboxMessages.Add(LoanEvents.Create(loan, type, dispatch.EventId, "reminder-" + dispatch.EventId.ToString("N"), now));
                await db.SaveChangesAsync(ct); dispatched++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { logger.LogWarning("Reminder {EventType} deferred loan {LoanId}; errorType={ErrorType}", type, id, e.GetType().Name); }
            finally { db.ChangeTracker.Clear(); }
        }
        logger.LogInformation("{Job} scanned {Candidates} loans, dispatched {Dispatched}", overdue ? "OverdueDetectionJob" : "DueReminderJob", candidates.Count, dispatched);
        return dispatched;
    }
}

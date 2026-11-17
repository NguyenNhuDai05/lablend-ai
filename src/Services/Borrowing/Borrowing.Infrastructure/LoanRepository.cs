using Borrowing.Application;
using Borrowing.Domain;
using LabLend.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Borrowing.Infrastructure;

public sealed class LoanRepository(BorrowingDbContext db) : ILoanRepository
{
    private IQueryable<LoanRequest> Full => db.LoanRequests.Include(l => l.Items).Include(l => l.Histories).AsSplitQuery();
    public async Task<LoanRequest?> FindSubmission(Guid studentId, Guid key, string hash, CancellationToken ct)
    {
        var submission = await db.LoanSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.StudentId == studentId && s.Key == key, ct);
        if (submission == null) return null;
        if (submission.RequestHash != hash) throw new LoanFailure(409, "Idempotency key already used for a different loan request.");
        return await Get(submission.LoanId, ct) ?? throw new LoanFailure(503, "Submitted loan is unavailable.");
    }
    public async Task<LoanRequest> AddSubmission(LoanRequest loan, Guid key, string hash, CancellationToken ct, string correlationId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Across replicas: only this student's draft is serialized. No HTTP call is made under this lock.
        var scope = $"loan-submission:{loan.StudentId:D}:{key:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scope}, 0))", ct);
        if (await FindSubmission(loan.StudentId, key, hash, ct) is { } previous) return previous;
        db.EventCorrelationId = correlationId;
        db.LoanRequests.Add(loan);
        db.LoanSubmissions.Add(new() { StudentId = loan.StudentId, Key = key, RequestHash = hash, LoanId = loan.Id });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return loan;
    }
    public async Task Add(LoanRequest loan, CancellationToken ct, string? correlationId = null)
    {
        if (correlationId != null) db.EventCorrelationId = correlationId;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.LoanRequests.Add(loan);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public Task<LoanRequest?> Get(Guid id, CancellationToken ct) => Full.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id, ct);
    public async Task<PagedResponse<LoanRequest>> List(Guid? studentId, LoanStatus? status, int page, int size, CancellationToken ct)
    {
        var query = Full.AsNoTracking().Where(l => (studentId == null || l.StudentId == studentId) && (status == null || l.Status == status));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(rows, total, page, size);
    }
    public async Task<LoanRequest> Mutate(Guid id, Action<LoanRequest> mutation, CancellationToken ct, string? correlationId = null)
    {
        if (correlationId != null) db.EventCorrelationId = correlationId;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lock only this aggregate. The second contender reads the committed lifecycle state after waiting.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"LoanRequests\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        var loan = await Full.SingleOrDefaultAsync(l => l.Id == id, ct) ?? throw new LoanFailure(404, "Loan not found.");
        mutation(loan);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new LoanFailure(409, "Loan changed; retry the operation."); }
        await transaction.CommitAsync(ct);
        return loan;
    }
}

using Identity.Application;
using Identity.Domain;
using LabLend.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Identity.Infrastructure;

public sealed class IdentityRepository(IdentityDbContext db) : IIdentityRepository
{
    public async Task<T> Mutate<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Database-wide account lock: short local operations; protects rotation and last-admin checks across replicas.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2321001)", ct);
        // Authentication may have loaded this account before the lock. Re-read after waiting for another writer.
        db.ChangeTracker.Clear();
        try
        {
            var result = await work();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new IdentityFailure(409, "An account with these identifiers already exists."); }
        catch (DbUpdateConcurrencyException) { throw new IdentityFailure(409, "Account changed; retry the operation."); }
    }
    public Task<RefreshToken?> FindToken(string hash, CancellationToken ct) => db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
    public void AddToken(RefreshToken token) => db.RefreshTokens.Add(token);
    public async Task Revoke(Guid userId, Guid? familyId, string reason, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId &&
            (familyId == null || t.TokenFamilyId == familyId) && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in tokens) { token.RevokedAt = DateTimeOffset.UtcNow; token.RevocationReason = reason; }
    }
    public void Audit(Guid? userId, Guid? actor, string action) => db.UserAuditEvents.Add(new() { UserId = userId, ActorUserId = actor, Action = action });
    public async Task<(IReadOnlyList<ApplicationUser> Users, long Total)> List(int page, int size, CancellationToken ct) =>
        (await db.Users.AsNoTracking().OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct), await db.Users.LongCountAsync(ct));
    public Task<int> ActiveAdminCount(CancellationToken ct) =>
        (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
         where u.IsActive && r.Name == Roles.Admin select u.Id).Distinct().CountAsync(ct);
}

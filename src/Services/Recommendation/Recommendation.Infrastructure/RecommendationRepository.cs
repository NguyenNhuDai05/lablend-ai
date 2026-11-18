using LabLend.Contracts;
using Microsoft.EntityFrameworkCore;
using Recommendation.Application;
using Recommendation.Domain;

namespace Recommendation.Infrastructure;

public sealed class RecommendationRepository(RecommendationDbContext db) : IRecommendationRepository
{
    public async Task Save(RecommendationSession session, CancellationToken ct) { db.Sessions.Add(session); await db.SaveChangesAsync(ct); }
    public Task<RecommendationSession?> Get(Guid id, Guid userId, CancellationToken ct) => db.Sessions.AsNoTracking().Include(s => s.Items)
        .SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
    public async Task<PagedResponse<RecommendationHistoryResponse>> History(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Sessions.AsNoTracking().Where(s => s.UserId == userId);
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new RecommendationHistoryResponse(s.Id, s.Purpose, s.CreatedAt, s.ModelUsed, s.Items.Count)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }
    public async Task<bool> TryConsumeDaily(Guid userId, DateOnly day, int limit, CancellationToken ct)
    {
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RecommendationDailyUsage" ("UserId", "Day", "Attempts") VALUES ({userId}, {day}, 1)
            ON CONFLICT ("UserId", "Day") DO UPDATE SET "Attempts" = "RecommendationDailyUsage"."Attempts" + 1
            WHERE "RecommendationDailyUsage"."Attempts" < {limit}
            """, ct);
        return changed == 1;
    }
}

using Microsoft.EntityFrameworkCore;
using Recommendation.Domain;
using Recommendation.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Recommendation.Tests;

public sealed class RecommendationPostgresFactAttribute : FactAttribute
{
    public RecommendationPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LABLEND_POSTGRES_TESTS") != "1") Skip = "Requires Docker: set LABLEND_POSTGRES_TESTS=1 for isolated PostgreSQL migrations/constraints tests.";
    }
}
public sealed class RecommendationDatabaseTests
{
    [RecommendationPostgresFact]
    public async Task PostgreSQL_migrations_save_items_ownership_page_FK_unique_rank_and_atomic_daily_quota()
    {
        // A new disposable container; never connects to the user's .env databases.
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<RecommendationDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new RecommendationDbContext(options); await db.Database.MigrateAsync();
        var repository = new RecommendationRepository(db);
        var user = Guid.NewGuid(); var other = Guid.NewGuid();
        var session = new RecommendationSession(user, "Thử nghiệm thiết bị phòng lab", ["Beginner"], null, "mock-test-only", DateTimeOffset.UtcNow);
        session.AddItem(Guid.NewGuid(), "Lý do", "Cách dùng"); await repository.Save(session, default);
        await repository.Save(new(other, "Thử nghiệm riêng của người khác", [], null, "mock-test-only", DateTimeOffset.UtcNow), default);
        Assert.Single((await repository.Get(session.Id, user, default))!.Items); Assert.Null(await repository.Get(session.Id, other, default));
        var page = await repository.History(user, 1, 1, default); Assert.Equal(1, page.Total); Assert.Single(page.Items);
        Assert.Empty((await repository.History(user, 2, 1, default)).Items);
        var item = session.Items.Single();
        async Task Constraint(Guid parent, Guid equipmentId, int rank)
        {
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "RecommendationItems" ("Id","SessionId","EquipmentTypeId","Rank","Reason","UsageNotes")
                VALUES ({Guid.NewGuid()},{parent},{equipmentId},{rank},'reason','usage')
                """));
        }
        await Constraint(Guid.NewGuid(), Guid.NewGuid(), 1); // FK
        await Constraint(session.Id, item.EquipmentTypeId, 2); // Unique equipment per session
        await Constraint(session.Id, Guid.NewGuid(), 0); // Check rank
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ => {
            await using var independent = new RecommendationDbContext(options);
            return await new RecommendationRepository(independent).TryConsumeDaily(user, day, 5, default);
        }));
        Assert.Equal(5, results.Count(r => r));
        Assert.False(await repository.TryConsumeDaily(user, day, 5, default));
        Assert.True(await repository.TryConsumeDaily(other, day, 5, default));
        Assert.True(await repository.TryConsumeDaily(user, day.AddDays(1), 5, default));
    }
}

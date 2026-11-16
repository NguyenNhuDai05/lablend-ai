using Identity.Domain;
using Identity.Infrastructure;
using Borrowing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LabLend.Sprint1.IntegrationTests;

[Collection("postgres")]
public sealed class SchemaTests(PostgresFixture postgres)
{
    [PostgresFact]
    public async Task Migrations_are_repeatable_unique_email_is_enforced_and_database_accounts_are_isolated()
    {
        await using var identity = new IdentityFactory(postgres); using var browser = identity.Browser();
        var user = await IdentityTests.RegisterLogin(browser);
        using var scope = identity.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        var existing = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), Email = existing.Email, NormalizedEmail = existing.NormalizedEmail,
            UserName = Guid.NewGuid().ToString(), NormalizedUserName = Guid.NewGuid().ToString() });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        db.ChangeTracker.Clear();
        await using var borrowing = new BorrowingFactory(postgres); using var client = borrowing.CreateClient();
        using var borrowingScope = borrowing.Services.CreateScope(); var loans = borrowingScope.ServiceProvider.GetRequiredService<BorrowingDbContext>();
        await loans.Database.MigrateAsync(); await loans.Database.MigrateAsync();
        Assert.Single(await loans.Database.GetAppliedMigrationsAsync());
        foreach (var source in new[] { postgres.IdentityConnection, postgres.BorrowingConnection })
        {
            var other = new NpgsqlConnectionStringBuilder(source);
            other.Database = other.Database == "identity_db" ? "borrowing_db" : "identity_db";
            await using var denied = new NpgsqlConnection(other.ConnectionString);
            var deniedError = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, deniedError.SqlState);
        }
    }
}

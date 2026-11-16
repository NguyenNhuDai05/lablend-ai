using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Borrowing.Infrastructure;

public static class BorrowingMigrations
{
    // Both API and worker migrate the SAME owned database; serialize startup so fresh Compose is repeatable.
    public static async Task Apply(BorrowingDbContext db, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtextextended('borrowing-schema-migrations', 0))", connection);
        await command.ExecuteNonQueryAsync(ct);
        try { await db.Database.MigrateAsync(ct); }
        finally
        {
            command.CommandText = "SELECT pg_advisory_unlock(hashtextextended('borrowing-schema-migrations', 0))";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}

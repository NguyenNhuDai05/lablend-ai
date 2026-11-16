using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Borrowing.Infrastructure;

// Session-level lock, deliberately outside any transaction. RPCs never hold a DB transaction open.
// All Borrowing API/worker instances use the same loan key. A dead process releases its session.
internal sealed class LoanLock(NpgsqlConnection connection, string key) : IAsyncDisposable
{
    public static async Task<LoanLock?> TryAcquire(BorrowingDbContext db, Guid loanId, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(ct);
            var key = "borrowing-loan:" + loanId;
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@key, 0))", connection);
            command.Parameters.AddWithValue("key", key);
            if ((bool)(await command.ExecuteScalarAsync(ct))!) return new(connection, key);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@key, 0))", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteScalarAsync();
        }
        finally { await connection.DisposeAsync(); }
    }
}

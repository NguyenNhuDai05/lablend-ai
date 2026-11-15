using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;

namespace LabLend.Observability;

public sealed class PostgresReadinessCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database connection and SELECT 1 succeeded; schema is not checked.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Do not attach raw connection exceptions: health responses/logs must not leak credentials.
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
    }
}

public sealed class RedisReadinessCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.ConnectTimeout = 2000;
            options.AsyncTimeout = 2000;
            options.AbortOnConnectFail = true;
            // Health-only connection; the future publisher/consumer must use its own singleton multiplexer.
            using var connection = await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(cancellationToken);
            await connection.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy("Redis PING succeeded; streams processing is not checked.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Redis is unavailable.");
        }
    }
}

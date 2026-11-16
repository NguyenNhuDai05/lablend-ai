using System.Collections.Concurrent;
using Borrowing.Application;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LabLend.Borrowing.Worker;

public sealed class JobProgress(TimeProvider clock) : IHealthCheck
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, TimeSpan Interval)> successes = new();
    private static readonly string[] RequiredJobs = [nameof(DueReminderJob), nameof(OverdueDetectionJob), nameof(ReservationReconciliationJob), nameof(OutboxPublisherJob)];
    public void Succeeded(string name, TimeSpan interval) => successes[name] = (clock.GetUtcNow(), interval);
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) => Task.FromResult(
        RequiredJobs.All(name => successes.TryGetValue(name, out var pass) && clock.GetUtcNow() - pass.At < TimeSpan.FromSeconds(Math.Max(60, pass.Interval.TotalSeconds * 4)))
            ? HealthCheckResult.Healthy("All borrowing jobs completed recent passes.") : HealthCheckResult.Unhealthy("A borrowing job has not completed a recent pass."));
}

public sealed class InventoryReadiness(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IInventoryClient>().Status(Guid.NewGuid(), "worker-readiness", cancellationToken);
            return HealthCheckResult.Healthy("Authenticated Inventory status RPC succeeded.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return HealthCheckResult.Unhealthy("Inventory is unavailable."); }
    }
}

public sealed class DependencyHeartbeatWorker(HealthCheckService health, ILogger<DependencyHeartbeatWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeat = configuration["Worker:HeartbeatPath"] ?? Path.Combine(Path.GetTempPath(), "lablend-worker.healthy");
        if (File.Exists(heartbeat)) File.Delete(heartbeat);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Worker:HeartbeatSeconds", 15), 1, 60)));
        try
        {
            do
            {
                var report = await health.CheckHealthAsync(stoppingToken);
                if (report.Status == HealthStatus.Healthy) await File.WriteAllTextAsync(heartbeat, DateTimeOffset.UtcNow.ToString("O"), stoppingToken);
                else if (File.Exists(heartbeat)) File.Delete(heartbeat);
                logger.LogInformation("Borrowing worker readiness {Status}; businessJobsImplemented=true", report.Status);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        finally { if (File.Exists(heartbeat)) File.Delete(heartbeat); }
    }
}

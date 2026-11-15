using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LabLend.Borrowing.Worker;

// Foundation process: verifies dependencies and writes a health heartbeat, not a business job.
public sealed class FoundationWorker(HealthCheckService health, ILogger<FoundationWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeat = configuration["Worker:HeartbeatPath"] ?? Path.Combine(Path.GetTempPath(), "lablend-worker.healthy");
        logger.LogWarning("Sprint 0 worker: overdue scan, reconciliation and Outbox publisher are Planned, not implemented.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            var report = await health.CheckHealthAsync(stoppingToken);
            if (report.Status == HealthStatus.Healthy)
                await File.WriteAllTextAsync(heartbeat, DateTimeOffset.UtcNow.ToString("O"), stoppingToken);
            else if (File.Exists(heartbeat))
                File.Delete(heartbeat);
            logger.LogInformation("Foundation dependency check {Status}; businessJobsImplemented=false", report.Status);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

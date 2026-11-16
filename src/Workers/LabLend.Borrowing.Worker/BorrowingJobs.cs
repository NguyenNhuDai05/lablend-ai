using Borrowing.Infrastructure;
using Microsoft.Extensions.Options;

namespace LabLend.Borrowing.Worker;

public abstract class ScopedJob(IServiceScopeFactory scopes, JobProgress progress, ILogger logger, string name, TimeSpan interval) : BackgroundService
{
    protected abstract Task Run(IServiceProvider services, CancellationToken ct);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await Run(scope.ServiceProvider, stoppingToken);
                progress.Succeeded(name, interval);
                logger.LogInformation("{Job} pass completed", name);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogError("{Job} pass failed; errorType={ErrorType}", name, e.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class DueReminderJob(IServiceScopeFactory scopes, JobProgress progress, IOptions<ReminderOptions> options, ILogger<DueReminderJob> logger)
    : ScopedJob(scopes, progress, logger, nameof(DueReminderJob), TimeSpan.FromSeconds(options.Value.PollSeconds))
{
    protected override async Task Run(IServiceProvider services, CancellationToken ct) => await services.GetRequiredService<LoanReminderJobs>().Run(false, ct);
}
public sealed class OverdueDetectionJob(IServiceScopeFactory scopes, JobProgress progress, IOptions<ReminderOptions> options, ILogger<OverdueDetectionJob> logger)
    : ScopedJob(scopes, progress, logger, nameof(OverdueDetectionJob), TimeSpan.FromSeconds(options.Value.PollSeconds))
{
    protected override async Task Run(IServiceProvider services, CancellationToken ct) => await services.GetRequiredService<LoanReminderJobs>().Run(true, ct);
}
public sealed class ReservationReconciliationJob(IServiceScopeFactory scopes, JobProgress progress, IOptions<BorrowingWorkflowOptions> options, ILogger<ReservationReconciliationJob> logger)
    : ScopedJob(scopes, progress, logger, nameof(ReservationReconciliationJob), TimeSpan.FromSeconds(options.Value.PollSeconds))
{
    protected override Task Run(IServiceProvider services, CancellationToken ct) => services.GetRequiredService<LoanReconciliation>().Run(ct);
}
public sealed class OutboxPublisherJob(IServiceScopeFactory scopes, JobProgress progress, IOptions<OutboxOptions> options, ILogger<OutboxPublisherJob> logger)
    : ScopedJob(scopes, progress, logger, nameof(OutboxPublisherJob), TimeSpan.FromSeconds(options.Value.PollSeconds))
{
    protected override async Task Run(IServiceProvider services, CancellationToken ct) => await services.GetRequiredService<OutboxPublisher>().Run(ct);
}

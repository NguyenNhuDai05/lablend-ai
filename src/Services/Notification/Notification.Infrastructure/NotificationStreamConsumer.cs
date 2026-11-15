using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notification.Application;
using StackExchange.Redis;

namespace Notification.Infrastructure;

public sealed class NotificationStreamConsumer(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILoanEventMapper mapper,
    IOptions<NotificationStreamOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationStreamConsumer> logger) : BackgroundService
{
    private readonly NotificationStreamOptions _options = options.Value;
    private readonly string _consumer = $"notification-{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var database = redis.GetDatabase();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureGroupAsync(database);
                await RecoverPendingAsync(database, stoppingToken);
                var entries = await database.StreamReadGroupAsync(_options.Stream, _options.Group, _consumer, ">", _options.BatchSize);
                foreach (var entry in entries) await ProcessAsync(database, entry, stoppingToken);
                if (entries.Length == 0) await Task.Delay(_options.PollDelayMilliseconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (RedisException exception)
            {
                logger.LogWarning(exception, "Redis unavailable while consuming notification stream; retrying without acknowledging messages.");
                await DelayAfterFailure(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected notification consumer loop failure.");
                await DelayAfterFailure(stoppingToken);
            }
        }
    }

    private async Task EnsureGroupAsync(IDatabase database)
    {
        try { await database.StreamCreateConsumerGroupAsync(_options.Stream, _options.Group, "0-0", createStream: true); }
        catch (RedisServerException exception) when (exception.Message.Contains("BUSYGROUP", StringComparison.Ordinal)) { }
    }

    private async Task RecoverPendingAsync(IDatabase database, CancellationToken cancellationToken)
    {
        var result = await database.StreamAutoClaimAsync(_options.Stream, _options.Group, _consumer,
            _options.PendingIdleMilliseconds, "0-0", _options.BatchSize);
        foreach (var entry in result.ClaimedEntries) await ProcessAsync(database, entry, cancellationToken);
    }

    private async Task ProcessAsync(IDatabase database, StreamEntry entry, CancellationToken cancellationToken)
    {
        var json = entry.Values.FirstOrDefault(x => x.Name == _options.EventField).Value;
        try
        {
            if (json.IsNullOrEmpty) throw new InvalidDataException($"Missing stream field '{_options.EventField}'.");
            var incoming = mapper.Map(entry.Id!, json!);
            await using var scope = scopeFactory.CreateAsyncScope();
            var inbox = scope.ServiceProvider.GetRequiredService<INotificationInbox>();
            await inbox.PersistAsync(incoming, cancellationToken);
            await database.StreamAcknowledgeAsync(_options.Stream, _options.Group, entry.Id);
            await database.HashDeleteAsync(AttemptsKey, entry.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not RedisException)
        {
            var attempts = await database.HashIncrementAsync(AttemptsKey, entry.Id);
            logger.LogWarning(exception, "Notification event {StreamMessageId} failed on attempt {Attempts}.", entry.Id, attempts);
            if (attempts >= _options.MaxAttempts) await ParkAsync(database, entry, json, attempts, exception);
        }
    }

    private async Task ParkAsync(IDatabase database, StreamEntry entry, RedisValue json, long attempts, Exception exception)
    {
        await database.StreamAddAsync(_options.ParkingStream,
        [
            new("originalMessageId", entry.Id), new("event", json),
            new("failureType", exception.GetType().Name), new("attempts", attempts),
            new("parkedAt", timeProvider.GetUtcNow().ToString("O"))
        ]);
        await database.StreamAcknowledgeAsync(_options.Stream, _options.Group, entry.Id);
        await database.HashDeleteAsync(AttemptsKey, entry.Id);
        logger.LogError("Parked permanently failing notification event {StreamMessageId} after {Attempts} attempts.", entry.Id, attempts);
    }

    private RedisKey AttemptsKey => $"{_options.Stream}:{_options.Group}:attempts";
    private async Task DelayAfterFailure(CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(2), token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}

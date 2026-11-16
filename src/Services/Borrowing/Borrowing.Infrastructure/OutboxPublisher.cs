using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Borrowing.Infrastructure;

public sealed class OutboxOptions
{
    public string Stream { get; set; } = "lablend.events.v1";
    public int BatchSize { get; set; } = 20;
    public int PollSeconds { get; set; } = 2;
}

public static class OutboxDependencyInjection
{
    // Only the publisher host needs Redis; API business commits depend solely on borrowing_db.
    public static IServiceCollection AddBorrowingPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection("Outbox"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Stream) && o.BatchSize is >= 1 and <= 100
                && o.PollSeconds is >= 1 and <= 300, "Invalid Outbox configuration.").ValidateOnStart();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("ConnectionStrings__Redis is required for the publisher."));
            options.AbortOnConnectFail = false; options.ConnectTimeout = 2000; options.AsyncTimeout = 2000;
            options.ConnectRetry = 0; options.BacklogPolicy = BacklogPolicy.FailFast;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddScoped<OutboxPublisher>();
        return services;
    }
}

public sealed class OutboxPublisher(BorrowingDbContext db, IConnectionMultiplexer redis, IOptions<OutboxOptions> options,
    TimeProvider clock, ILogger<OutboxPublisher> logger)
{
    public async Task<int> Run(CancellationToken ct)
    {
        var published = 0;
        for (var index = 0; index < options.Value.BatchSize; index++)
        {
            ct.ThrowIfCancellationRequested();
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = clock.GetUtcNow();
            var rows = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM \"OutboxMessages\" WHERE \"PublishedAt\" IS NULL AND \"NextAttemptAt\" <= {now} ORDER BY \"OccurredAt\", \"EventId\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
            if (rows.Count == 0) break;
            var message = rows[0];
            message.Attempts++;
            try
            {
                await redis.GetDatabase().StreamAddAsync(options.Value.Stream, [new NameValueEntry("event", LoanEvents.Envelope(message))]).WaitAsync(ct);
                message.PublishedAt = clock.GetUtcNow(); message.LastError = null; published++;
                logger.LogInformation("Published {EventId} {EventType} loan {LoanId}; correlationId={CorrelationId}", message.EventId, message.EventType, message.AggregateId, message.CorrelationId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) when (e is RedisException or System.Text.Json.JsonException)
            {
                message.LastError = e.GetType().Name;
                message.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(message.Attempts, 8))));
                logger.LogWarning("Outbox {EventId} deferred after {Attempts} attempts; errorType={ErrorType}; correlationId={CorrelationId}", message.EventId, message.Attempts, message.LastError, message.CorrelationId);
            }
            // XADD acknowledgement precedes this marker: crash here can duplicate, never silently remove an event.
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        return published;
    }
}

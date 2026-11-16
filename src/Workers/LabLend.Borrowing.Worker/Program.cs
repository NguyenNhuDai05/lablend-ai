using Borrowing.Infrastructure;
using LabLend.Borrowing.Worker;
using LabLend.Observability;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddBorrowingInfrastructure(builder.Configuration);
builder.Services.AddBorrowingPublisher(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration, redis: true);
builder.Services.AddOptions<InventoryOptions>().Validate(o => o.ServiceToken.Length >= 32, "Inventory service credential is required.").ValidateOnStart();
builder.Services.AddSingleton<JobProgress>();
builder.Services.AddHealthChecks().AddCheck<JobProgress>("borrowing-jobs").AddCheck<InventoryReadiness>("inventory");
builder.Services.AddHostedService<DueReminderJob>();
builder.Services.AddHostedService<OverdueDetectionJob>();
builder.Services.AddHostedService<ReservationReconciliationJob>();
builder.Services.AddHostedService<OutboxPublisherJob>();
builder.Services.AddHostedService<DependencyHeartbeatWorker>();
using var host = builder.Build();
if (builder.Configuration.GetValue("Database:ApplyMigrations", true))
{
    await using var scope = host.Services.CreateAsyncScope();
    await BorrowingMigrations.Apply(scope.ServiceProvider.GetRequiredService<BorrowingDbContext>());
}
await host.RunAsync();

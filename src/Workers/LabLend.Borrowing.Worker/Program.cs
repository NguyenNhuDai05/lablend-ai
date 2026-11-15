using Borrowing.Infrastructure;
using LabLend.Borrowing.Worker;
using LabLend.Observability;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddBorrowingInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration, redis: true);
builder.Services.AddHostedService<FoundationWorker>();
await builder.Build().RunAsync();

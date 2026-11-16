using Inventory.Application;
using Inventory.Host;
using Inventory.Host.Grpc;
using Inventory.Infrastructure;
using LabLend.Observability;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
if ((builder.Configuration["Inventory:ServiceToken"]?.Length ?? 0) < 32)
    throw new InvalidOperationException("Inventory__ServiceToken (>= 32 chars) is required. Run scripts/Initialize-Environment.ps1.");
builder.AddLabLendApi("Inventory");
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddScoped<AssetService>().AddScoped<AllocationService>();
builder.Services.Configure<MvcOptions>(options => options.Filters.Add<InventoryExceptionFilter>());
builder.Services.AddLabLendDependencies(builder.Configuration);
builder.Services.AddGrpc()
    .AddServiceOptions<InventoryAllocationGrpcService>(options => options.Interceptors.Add<InventoryGrpcInterceptor>());
builder.Services.AddGrpcHealthChecks();
var app = builder.Build();
app.Services.MigrateInventoryDatabase();
app.UseLabLendApi("Inventory");
app.MapGrpcService<InventoryAllocationGrpcService>();
app.MapGrpcHealthChecksService();
app.Run();

using Inventory.Infrastructure;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Inventory");
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
builder.Services.AddGrpc();
// Standard grpc.health.v1.Health only. Business schema belongs to Member 2.
builder.Services.AddGrpcHealthChecks();
var app = builder.Build();
app.UseLabLendApi("Inventory");
app.MapGrpcHealthChecksService();
app.Run();

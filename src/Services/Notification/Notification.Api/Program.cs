using Notification.Infrastructure;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Notification");
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration, redis: true);
var app = builder.Build();
app.UseLabLendApi("Notification");
app.Run();

using Notification.Infrastructure;
using Notification.Application;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Notification");
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<ILoanEventMapper, ProposedLoanEventMapper>();
builder.Services.AddLabLendDependencies(builder.Configuration, redis: true);
var app = builder.Build();
app.UseLabLendApi("Notification");
app.Run();

using Notification.Infrastructure;
using Notification.Application;
using LabLend.Observability;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Notification");
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<ILoanEventMapper, ProposedLoanEventMapper>();
builder.Services.AddLabLendDependencies(builder.Configuration, redis: true);
var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}
app.UseLabLendApi("Notification");
app.Run();

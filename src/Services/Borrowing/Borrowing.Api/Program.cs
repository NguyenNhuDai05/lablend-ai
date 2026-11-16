using Borrowing.Infrastructure;
using LabLend.Observability;
using Borrowing.Api;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Borrowing");
builder.Services.AddBorrowingInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
builder.Services.AddExceptionHandler<LoanExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await BorrowingMigrations.Apply(scope.ServiceProvider.GetRequiredService<BorrowingDbContext>());
}
app.UseLabLendApi("Borrowing");
app.Run();

namespace Borrowing.Api { public sealed class BorrowingMarker; }

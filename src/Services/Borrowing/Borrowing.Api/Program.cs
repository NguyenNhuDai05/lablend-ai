using Borrowing.Infrastructure;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Borrowing");
builder.Services.AddBorrowingInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
var app = builder.Build();
app.UseLabLendApi("Borrowing");
app.Run();

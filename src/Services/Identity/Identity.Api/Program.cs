using Identity.Infrastructure;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Identity");
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
var app = builder.Build();
app.UseLabLendApi("Identity");
app.Run();

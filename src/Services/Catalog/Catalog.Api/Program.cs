using Catalog.Infrastructure;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Catalog");
builder.Services.AddCatalogInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
var app = builder.Build();
app.UseLabLendApi("Catalog");
app.Run();

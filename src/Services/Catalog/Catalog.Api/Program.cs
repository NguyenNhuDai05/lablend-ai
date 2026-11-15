using Catalog.Api;
using Catalog.Application;
using Catalog.Infrastructure;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Catalog");
builder.Services.AddCatalogInfrastructure(builder.Configuration);
builder.Services.AddScoped<CategoryService>().AddScoped<EquipmentTypeService>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options => options.Filters.Add<CatalogExceptionFilter>());
builder.Services.AddLabLendDependencies(builder.Configuration);
var app = builder.Build();
app.Services.MigrateCatalogDatabase();
app.UseLabLendApi("Catalog");
app.Run();

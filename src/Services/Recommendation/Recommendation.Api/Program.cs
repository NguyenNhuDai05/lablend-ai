using Recommendation.Infrastructure;
using LabLend.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.AddLabLendApi("Recommendation");
builder.Services.AddRecommendationInfrastructure(builder.Configuration);
builder.Services.AddLabLendDependencies(builder.Configuration);
var app = builder.Build();
app.UseLabLendApi("Recommendation");
app.Run();

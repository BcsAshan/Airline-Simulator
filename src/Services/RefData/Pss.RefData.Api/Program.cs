using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Observability;
using Pss.BuildingBlocks.Persistence;
using Pss.BuildingBlocks.Session;
using Pss.RefData.Api.Endpoints;
using Pss.RefData.Api.Infrastructure;

// Reference Data & Content service (blueprint §7.2).
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("refdata");
builder.AddPssApi("Pss.RefData");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddPssSqlServer<RefDataDbContext>(builder.Configuration);
builder.Services.AddPssRedisSessions(builder.Configuration);
builder.Services.AddPssAuthentication(builder.Configuration);

var app = builder.Build();
app.UsePssApi();
app.UsePssAuthentication();
app.MapPssHealth();
app.MapResourceEndpoints();

await app.InitializeDatabaseAsync<RefDataDbContext>((db, _) => RefDataDbContext.SeedAsync(db));
app.Run();

using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Observability;
using Pss.BuildingBlocks.Persistence;
using Pss.BuildingBlocks.Session;
using Pss.Pricing.Api.Endpoints;
using Pss.Pricing.Api.Infrastructure;

// Pricing service (blueprint §7.4, §14): pure pricing over a cached configuration snapshot.
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("pricing");
builder.AddPssApi("Pss.Pricing");
builder.Services.AddMemoryCache();
builder.Services.AddPssSqlServer<PricingDbContext>(builder.Configuration);
builder.Services.AddPssRedisSessions(builder.Configuration);
builder.Services.AddPssAuthentication(builder.Configuration);
builder.Services.AddSingleton<PricingSnapshotProvider>();

var app = builder.Build();
app.UsePssApi();
app.UsePssAuthentication();
app.MapPssHealth();
app.MapInternalPricingEndpoints();

await app.InitializeDatabaseAsync<PricingDbContext>((db, _) => PricingDbContext.SeedAsync(db));
app.Run();

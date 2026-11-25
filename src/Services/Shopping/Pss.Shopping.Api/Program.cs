using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Observability;
using Pss.BuildingBlocks.Session;
using Pss.Shopping.Api.Application;
using Pss.Shopping.Api.Endpoints;
using Pss.Shopping.Api.Infrastructure;

// Shopping (availability) service (blueprint §7.5). Stateless; offers cached in Redis.
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("shopping");
builder.AddPssApi("Pss.Shopping");
builder.Services.AddPssRedisSessions(builder.Configuration);
builder.Services.AddPssAuthentication(builder.Configuration);
builder.Services.AddInternalServiceClient<IInventoryClient, InventoryClient>(builder.Configuration, "Inventory", "http://localhost:5103");
builder.Services.AddInternalServiceClient<IPricingClient, PricingClient>(builder.Configuration, "Pricing", "http://localhost:5104");
builder.Services.AddSingleton<OfferStore>();
builder.Services.AddScoped<AvailabilityService>();

var app = builder.Build();
app.UsePssApi();
app.UsePssAuthentication();
app.MapPssHealth();
app.MapAvailabilityEndpoints();
app.Run();

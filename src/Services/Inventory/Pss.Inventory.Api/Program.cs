using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Messaging;
using Pss.BuildingBlocks.Observability;
using Pss.BuildingBlocks.Persistence;
using Pss.BuildingBlocks.Session;
using Pss.Inventory.Api.Application;
using Pss.Inventory.Api.Consumers;
using Pss.Inventory.Api.Endpoints;
using Pss.Inventory.Api.Infrastructure;

// Inventory & Schedule service (blueprint §7.3, §15).
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("inventory");
builder.AddPssApi("Pss.Inventory");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPssSqlServer<InventoryDbContext>(builder.Configuration);
builder.Services.AddPssRedisSessions(builder.Configuration);
builder.Services.AddPssAuthentication(builder.Configuration);
builder.Services.AddPssMessaging(builder.Configuration, "inventory", x => x.AddConsumer<ReleaseHoldsOnSessionExpiredConsumer>());
builder.Services.AddScoped<InventoryService>();
builder.Services.AddHostedService<HoldExpirySweeper>();

var app = builder.Build();
app.UsePssApi();
app.UsePssAuthentication();
app.MapPssHealth();
app.MapInternalInventoryEndpoints();

await app.InitializeDatabaseAsync<InventoryDbContext>((db, sp) =>
    InventorySeeder.SeedAsync(db, sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<TimeProvider>()));
app.Run();

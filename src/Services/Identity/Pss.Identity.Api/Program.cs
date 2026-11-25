using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Messaging;
using Pss.BuildingBlocks.Observability;
using Pss.BuildingBlocks.Persistence;
using Pss.BuildingBlocks.Session;
using Pss.Identity.Api.Application;
using Pss.Identity.Api.Endpoints;
using Pss.Identity.Api.Infrastructure;

// Identity & Session service (blueprint §7.1): tokens, sessions in Redis, JWKS, session-expiry events.
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("identity");
builder.AddPssApi("Pss.Identity");

var keyStore = new SigningKeyStore(builder.Configuration["Jwt:KeyPath"] ?? "keys/identity-signing-key.pem");
builder.Services.AddSingleton(keyStore);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPssSqlServer<IdentityDbContext>(builder.Configuration);
builder.Services.AddPssRedisSessions(builder.Configuration);
builder.Services.AddPssAuthentication(builder.Configuration, keyStore.Key);
builder.Services.AddPssMessaging(builder.Configuration, "identity");
builder.Services.AddScoped<TokenService>();
builder.Services.AddHostedService<SessionExpirySweeper>();

var app = builder.Build();
app.UsePssApi();
app.UsePssAuthentication();
app.MapPssHealth();
app.MapWellKnownEndpoints();
app.MapTokenEndpoints();

await app.InitializeDatabaseAsync<IdentityDbContext>((db, _) => IdentitySeeder.SeedAsync(db));
app.Run();

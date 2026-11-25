using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Messaging;
using Pss.BuildingBlocks.Observability;
using Pss.BuildingBlocks.Persistence;
using Pss.BuildingBlocks.Session;
using Pss.Payment.Api.Application;
using Pss.Payment.Api.Endpoints;
using Pss.Payment.Api.Infrastructure;

// Payment service (blueprint §7.9, §16) — the only PCI-scoped component.
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("payment");
builder.AddPssApi("Pss.Payment");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPssSqlServer<PaymentDbContext>(builder.Configuration);
builder.Services.AddPssRedisSessions(builder.Configuration);
builder.Services.AddPssAuthentication(builder.Configuration);
builder.Services.AddPssMessaging<PaymentDbContext>(builder.Configuration, "payment");
builder.Services.AddInternalServiceClient<IBookingDraftClient, BookingDraftClient>(builder.Configuration, "Booking", "http://localhost:5106");
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddSingleton<PaymentMethodCatalog>();
builder.Services.AddScoped<PaymentService>();

var app = builder.Build();
app.UsePssApi();
app.UsePssAuthentication();
app.MapPssHealth();
app.MapPaymentEndpoints();

await app.InitializeDatabaseAsync<PaymentDbContext>();
app.Run();

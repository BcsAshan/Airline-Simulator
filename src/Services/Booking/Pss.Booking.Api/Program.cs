using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Messaging;
using Pss.BuildingBlocks.Observability;
using Pss.BuildingBlocks.Persistence;
using Pss.BuildingBlocks.Session;
using Pss.Booking.Api.Application;
using Pss.Booking.Api.Consumers;
using Pss.Booking.Api.Endpoints;
using Pss.Booking.Api.Infrastructure;

// Booking service — the core (blueprint §5, §7.6, §12). Owns drafts (Redis) and committed bookings (SQL).
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("booking");
builder.AddPssApi("Pss.Booking");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddPssSqlServer<BookingDbContext>(builder.Configuration);
builder.Services.AddPssRedisSessions(builder.Configuration);
builder.Services.AddPssAuthentication(builder.Configuration);
builder.Services.AddPssMessaging<BookingDbContext>(builder.Configuration, "booking", x => x.AddConsumer<DropDraftOnSessionExpiredConsumer>());

builder.Services.AddInternalServiceClient<IShoppingClient, ShoppingClient>(builder.Configuration, "Shopping", "http://localhost:5105");
builder.Services.AddInternalServiceClient<IPricingClient, PricingClient>(builder.Configuration, "Pricing", "http://localhost:5104");
builder.Services.AddInternalServiceClient<IInventoryClient, InventoryClient>(builder.Configuration, "Inventory", "http://localhost:5103");
builder.Services.AddInternalServiceClient<IPaymentClient, PaymentClient>(builder.Configuration, "Payment", "http://localhost:5107");
builder.Services.AddInternalServiceClient<IRefDataClient, RefDataClient>(builder.Configuration, "RefData", "http://localhost:5102");
builder.Services.AddInternalServiceClient<ISeatClient, SeatClient>(builder.Configuration, "Seat", "http://localhost:5110");

builder.Services.AddSingleton<DraftStore>();
builder.Services.AddScoped<StatefulBookingExecutor>();
builder.Services.AddScoped<SellService>();
builder.Services.AddScoped<BookingCommands>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<CommitService>();
builder.Services.AddScoped<RetrieveService>();

var app = builder.Build();
app.UsePssApi();
app.UsePssAuthentication();
app.MapPssHealth();
app.MapBookingEndpoints();
app.MapInternalDraftEndpoints();

await app.InitializeDatabaseAsync<BookingDbContext>();
app.Run();

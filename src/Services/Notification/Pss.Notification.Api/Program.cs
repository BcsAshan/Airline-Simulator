using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Messaging;
using Pss.BuildingBlocks.Observability;
using Pss.Notification.Api.Consumers;
using Pss.Notification.Api.Infrastructure;

// Notification service (blueprint §7.15). Phase 1: itinerary email on BookingCommitted.
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("notification");
builder.AddPssApi("Pss.Notification");
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddPssMessaging(builder.Configuration, "notification", x => x.AddConsumer<ItineraryEmailConsumer>());

var app = builder.Build();
app.UsePssApi();
app.MapPssHealth();
app.Run();

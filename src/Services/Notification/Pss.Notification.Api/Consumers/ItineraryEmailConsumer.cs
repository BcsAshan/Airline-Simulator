using System.Globalization;
using System.Text;
using MassTransit;
using Pss.Contracts.Events;
using Pss.Notification.Api.Infrastructure;

namespace Pss.Notification.Api.Consumers;

/// <summary>Sends the itinerary when a new booking is committed and the primary contact chose email distribution (2).</summary>
public sealed class ItineraryEmailConsumer(IEmailSender email) : IConsumer<BookingCommitted>
{
    private const int EmailDistribution = 2;

    public async Task Consume(ConsumeContext<BookingCommitted> context)
    {
        var b = context.Message;
        if (!b.IsNewBooking || b.DistributionOption != EmailDistribution || string.IsNullOrWhiteSpace(b.ContactEmail)) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Dear {b.ContactFirstName} {b.ContactLastName},").AppendLine();
        sb.AppendLine($"Thank you for flying with Demo Air. Your booking reference is {b.RecordLocator}.").AppendLine();
        sb.AppendLine("Flights:");
        foreach (var s in b.Segments)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {s.CarrierCode}{s.FlightNumber}  {s.Origin} {s.DepartureLocal:ddMMMyy HH:mm} → {s.Destination} {s.ArrivalLocal:HH:mm}  class {s.ClassOfService}"));
        sb.AppendLine().AppendLine("Passengers:");
        foreach (var p in b.Passengers) sb.AppendLine($"  {p.LastName}/{p.FirstName} ({p.PassengerTypeCode})");
        sb.AppendLine().AppendLine(string.Create(CultureInfo.InvariantCulture, $"Total: {b.TotalAmount:0.00} {b.CurrencyCode}   Balance due: {b.BalanceDue:0.00} {b.CurrencyCode}"));

        await email.SendAsync(b.ContactEmail!, $"Your Demo Air booking {b.RecordLocator}", sb.ToString(), context.CancellationToken);
    }
}

using Pss.Contracts.Booking;
using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.Booking.Api.Domain;

/// <summary>
/// Breakdown is always derived, never set directly (blueprint §9 invariant 6). Recompute after every command.
/// </summary>
public static class BookingCalculator
{
    private static readonly HashSet<ChargeType> TaxTypes = new()
    {
        ChargeType.Tax, ChargeType.IncludedTax, ChargeType.TravelFee, ChargeType.IncludedTravelFee, ChargeType.FareSurcharge
    };

    private static readonly HashSet<ChargeType> DiscountTypes = new() { ChargeType.Discount, ChargeType.PromotionDiscount };

    public static void Recalculate(BookingModel booking)
    {
        var b = new BookingBreakdown();
        var paxCountByType = booking.Passengers.Values
            .GroupBy(p => p.PassengerTypeCode)
            .ToDictionary(g => g.Key, g => g.Count());

        // Journeys (fares, discounts, taxes) — passengerFares are per PTC, multiplied by the PTC count.
        foreach (var journey in booking.Journeys)
        {
            var jb = new JourneyBreakdown { JourneyKey = journey.JourneyKey };
            foreach (var fare in journey.Segments.SelectMany(s => s.Fares))
            foreach (var pf in fare.PassengerFares)
            {
                var count = paxCountByType.GetValueOrDefault(pf.PassengerType, 0);
                foreach (var sc in pf.ServiceCharges)
                {
                    var amount = sc.Amount * count;
                    jb.TotalAmount += amount;
                    if (TaxTypes.Contains(sc.Type)) jb.TotalTax += amount;
                    if (DiscountTypes.Contains(sc.Type)) jb.TotalDiscount += amount;
                }
            }
            jb.TotalAmount = Round(jb.TotalAmount);
            jb.TotalTax = Round(jb.TotalTax);
            jb.TotalDiscount = Round(jb.TotalDiscount);
            b.Journeys[journey.JourneyKey] = jb;
            b.JourneyTotals.TotalAmount += jb.TotalAmount;
            b.JourneyTotals.TotalTax += jb.TotalTax;
            b.JourneyTotals.TotalDiscount += jb.TotalDiscount;
        }

        // Passenger fees (services). Seats/SSRs/name changes get their own buckets in Phase 2.
        decimal feesTotal = 0;
        var allServices = new ChargeTotal();
        foreach (var pax in booking.Passengers.Values)
        {
            var pb = new PassengerBreakdown { PassengerKey = pax.PassengerKey };
            if (pax.Fees.Count > 0)
            {
                var ct = new ChargeTotal();
                foreach (var sc in pax.Fees.SelectMany(f => f.ServiceCharges))
                {
                    ct.Total += sc.Amount;
                    if (TaxTypes.Contains(sc.Type)) ct.Taxes += sc.Amount;
                    ct.Charges.Add(sc);
                }
                ct.Total = Round(ct.Total);
                pb.Services = ct;
                feesTotal += ct.Total;
                allServices.Total += ct.Total;
                allServices.Taxes += ct.Taxes;
                allServices.Charges.AddRange(ct.Charges);
            }
            b.Passengers[pax.PassengerKey] = pb;
        }
        if (allServices.Charges.Count > 0) b.PassengerTotals.Services = allServices;

        var total = Round(b.JourneyTotals.TotalAmount + feesTotal);
        var counted = booking.Payments.Where(CountsTowardsBalance).Sum(p => p.Amounts.Amount);
        var authorized = booking.Payments.Where(p => p.Status == BookingPaymentStatus.Approved).Sum(p => p.Amounts.Amount);

        b.TotalAmount = total;
        b.TotalToCollect = total;
        b.TotalCharged = total;
        b.BalanceDue = Round(total - counted);
        b.AuthorizedBalanceDue = Round(total - authorized);

        booking.Breakdown = b;
        booking.Info.PaidStatus = b.BalanceDue > 0 ? PaidStatus.UnderPaid : b.BalanceDue == 0 ? PaidStatus.PaidInFull : PaidStatus.OverPaid;
    }

    /// <summary>Declined payments never reduce the balance.</summary>
    public static bool CountsTowardsBalance(Payment p) => p.Status != BookingPaymentStatus.Declined;

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

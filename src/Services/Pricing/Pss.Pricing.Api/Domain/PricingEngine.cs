using Pss.BuildingBlocks.Keys;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;

namespace Pss.Pricing.Api.Domain;

/// <summary>Immutable configuration snapshot used by the engine (cached per pod, blueprint §14 "Performance").</summary>
public sealed class PricingSnapshot
{
    public required IReadOnlyList<Fare> Fares { get; init; }
    public required IReadOnlyList<TaxDefinition> Taxes { get; init; }
    public required IReadOnlyDictionary<string, decimal> PassengerTypeDiscounts { get; init; }
    public required IReadOnlyDictionary<string, string> StationCountries { get; init; }

    public IEnumerable<Fare> FaresFor(string carrier, string origin, string destination)
        => Fares.Where(f => !f.InActive && f.CarrierCode == carrier && f.Origin == origin && f.Destination == destination);
}

/// <summary>
/// Pure, deterministic pricing (blueprint §14): base fare → PTC discount → taxes/fees. Returns service charges only.
/// TODO(Phase 2): fare rules (Cat 1/2/5/16), promotions, currency conversion, through/connection adjustments.
/// </summary>
public static class PricingEngine
{
    public static PricedSegmentFare PriceSegment(Fare fare, string segmentRef, string origin, string destination, bool international,
        IEnumerable<string> passengerTypes, PricingSnapshot snapshot)
    {
        var originCountry = snapshot.StationCountries.GetValueOrDefault(origin, string.Empty);
        var priced = new PricedSegmentFare
        {
            SegmentRef = segmentRef,
            FareId = fare.FareId,
            FareKey = new FareKey(fare.FareId, fare.ClassOfService, fare.CarrierCode, fare.FareBasisCode, fare.RuleNumber, fare.FareSequence).Encode(),
            CarrierCode = fare.CarrierCode,
            ClassOfService = fare.ClassOfService,
            FareBasisCode = fare.FareBasisCode,
            RuleNumber = fare.RuleNumber,
            ProductClass = fare.ProductClass,
            TravelClassCode = fare.TravelClassCode,
            FareSequence = fare.FareSequence
        };

        foreach (var ptc in passengerTypes.Distinct())
        {
            var charges = new List<ServiceCharge> { Charge(ChargeType.FarePrice, null, null, fare.Amount, fare.CurrencyCode) };

            var discountPct = snapshot.PassengerTypeDiscounts.GetValueOrDefault(ptc, 0m);
            var discount = discountPct == 0 ? 0m : -Round(fare.Amount * discountPct / 100m);
            if (discount != 0) charges.Add(Charge(ChargeType.Discount, ptc, $"{ptc} discount {discountPct:0.##}%", discount, fare.CurrencyCode));
            var discounted = fare.Amount + discount;

            decimal taxesAndFees = 0;
            foreach (var tax in snapshot.Taxes.Where(t => Applies(t, originCountry, international, ptc)))
            {
                var amount = tax.IsPercent ? Round(discounted * tax.Amount / 100m) : tax.Amount;
                if (amount == 0) continue;
                charges.Add(Charge((ChargeType)tax.ChargeType, tax.Code, tax.Name, amount, fare.CurrencyCode, tax.Code));
                taxesAndFees += amount;
            }

            priced.PassengerFares.Add(new PricedPassengerFare
            {
                PassengerType = ptc,
                FareAmount = fare.Amount,
                DiscountedFare = discounted,
                TaxesAndFees = taxesAndFees,
                ServiceCharges = charges
            });
        }

        return priced;
    }

    /// <summary>
    /// Builds fare options for a journey: for each product class available on every segment, the cheapest
    /// class with enough seats per segment (sum-of-sector). Sorted by total price, limited to <paramref name="maxOptions"/>.
    /// </summary>
    public static List<PricedFareOption> PriceJourney(PriceJourneyInput journey, IReadOnlyList<PaxTypeCount> passengers,
        PricingSnapshot snapshot, int maxOptions)
    {
        var seatsNeeded = Math.Max(1, passengers.Where(p => p.Type != "INF").Sum(p => p.Count));
        var paxTypes = passengers.Where(p => p.Count > 0).Select(p => p.Type).Distinct().ToList();
        if (paxTypes.Count == 0) paxTypes.Add("ADT");

        var perSegment = journey.Segments.Select(seg => snapshot.FaresFor(seg.CarrierCode, seg.Origin, seg.Destination)
                .Where(f => seg.AvailableClasses.TryGetValue(f.ClassOfService, out var avail) && avail >= seatsNeeded)
                .Where(f => IsTravelDateValid(f, seg.DepartureLocal))
                .Select(f => (Fare: f, Available: seg.AvailableClasses[f.ClassOfService]))
                .ToList())
            .ToList();
        if (perSegment.Count == 0 || perSegment.Any(c => c.Count == 0)) return new List<PricedFareOption>();

        var productClasses = perSegment
            .Select(c => c.Select(x => x.Fare.ProductClass ?? string.Empty).ToHashSet())
            .Aggregate((a, b) => { a.IntersectWith(b); return a; });

        var options = new List<(decimal Total, PricedFareOption Option)>();
        foreach (var pc in productClasses)
        {
            var option = new PricedFareOption { ProductClass = string.IsNullOrEmpty(pc) ? null : pc, AvailableCount = int.MaxValue };
            decimal total = 0;
            for (var i = 0; i < journey.Segments.Count; i++)
            {
                var seg = journey.Segments[i];
                var cheapest = perSegment[i].Where(x => (x.Fare.ProductClass ?? string.Empty) == pc).OrderBy(x => x.Fare.Amount).First();
                var priced = PriceSegment(cheapest.Fare, seg.Ref, seg.Origin, seg.Destination, seg.International, paxTypes, snapshot);
                option.Segments.Add(priced);
                option.AvailableCount = Math.Min(option.AvailableCount, cheapest.Available);
                total += priced.PassengerFares.Sum(pf =>
                    pf.ServiceCharges.Sum(sc => sc.Amount) * passengers.Where(p => p.Type == pf.PassengerType).Sum(p => p.Count));
            }
            options.Add((total, option));
        }

        return options.OrderBy(o => o.Total).Take(Math.Max(1, maxOptions)).Select(o => o.Option).ToList();
    }

    public static bool Applies(TaxDefinition tax, string originCountry, bool international, string ptc)
    {
        if (tax.InActive) return false;
        if (tax.DepartureCountryCode is not null && tax.DepartureCountryCode != originCountry) return false;
        if (tax.Scope == "INT" && !international) return false;
        if (tax.Scope == "DOM" && international) return false;
        var exempt = tax.ExemptPassengerTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return !exempt.Contains(ptc, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTravelDateValid(Fare fare, DateTime departureLocal)
        => (fare.TravelFrom is null || departureLocal.Date >= fare.TravelFrom.Value.Date)
           && (fare.TravelTo is null || departureLocal.Date <= fare.TravelTo.Value.Date);

    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static ServiceCharge Charge(ChargeType type, string? code, string? detail, decimal amount, string currency, string? ticketCode = null)
        => new()
        {
            Type = type,
            Code = code,
            Detail = detail,
            Amount = amount,
            CurrencyCode = currency,
            ForeignCurrencyCode = currency,
            ForeignAmount = amount,
            TicketCode = ticketCode
        };
}

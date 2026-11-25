using System.Globalization;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;
using Pss.Shopping.Api.Infrastructure;
using Pss.Shopping.Api.Models;

namespace Pss.Shopping.Api.Application;

/// <summary>
/// Availability search (blueprint §7.5, §13.5): inventory segments → journeys (non-stop + 1-stop connections)
/// → one batched Pricing call → Navitaire-shaped response. Every fare offered is remembered in the offer store.
/// </summary>
public sealed class AvailabilityService(IInventoryClient inventory, IPricingClient pricing, OfferStore offers, IConfiguration configuration)
{
    private int DefaultMaxConnections => configuration.GetValue("Shopping:DefaultMaxConnections", 1);
    private int DefaultFaresPerJourney => configuration.GetValue("Shopping:DefaultFaresPerJourney", 1);
    private int MaxPassengers => configuration.GetValue("Shopping:MaxPassengers", 9);
    private TimeSpan MinConnection => TimeSpan.FromMinutes(configuration.GetValue("Shopping:MinConnectionMinutes", 60));
    private TimeSpan MaxConnection => TimeSpan.FromHours(configuration.GetValue("Shopping:MaxConnectionHours", 24));

    public Task<AvailabilityResponse> SearchSimpleAsync(SimpleSearchRequest request, SessionInfo session, CancellationToken ct)
    {
        var criterion = new SearchCriterion
        {
            Stations = { OriginStationCodes = { request.Origin }, DestinationStationCodes = { request.Destination } },
            Dates = { BeginDate = request.BeginDate, EndDate = request.EndDate },
            Filters = request.Filters
        };
        return SearchAsync(new List<SearchCriterion> { criterion }, request.Passengers, request.Codes?.CurrencyCode ?? request.CurrencyCode,
            request.Codes?.PromotionCode, request.NumberOfFaresPerJourney, request.TaxesAndFees, session, ct);
    }

    public Task<AvailabilityResponse> SearchFullAsync(FullSearchRequest request, SessionInfo session, CancellationToken ct)
        => SearchAsync(request.Criteria, request.Passengers, request.Codes?.CurrencyCode, request.Codes?.PromotionCode,
            request.NumberOfFaresPerJourney, request.TaxesAndFees, session, ct);

    private async Task<AvailabilityResponse> SearchAsync(IReadOnlyList<SearchCriterion> criteria, PassengerTypeCriteria? pax,
        string? currencyCode, string? promotionCode, int? faresPerJourney, TaxesAndFeesRollupMode? rollup, SessionInfo session, CancellationToken ct)
    {
        if (criteria.Count == 0) throw new DomainException(ErrorCodes.NullRequest, "At least one search criterion is required.");

        var passengers = (pax?.Types ?? new List<PaxTypeCount>())
            .Where(t => t.Count > 0)
            .Select(t => new PaxTypeCount { Type = t.Type.Trim().ToUpperInvariant(), Count = t.Count })
            .ToList();
        if (passengers.Count == 0) passengers.Add(new PaxTypeCount { Type = "ADT", Count = 1 });
        if (passengers.Sum(p => p.Count) > MaxPassengers)
            throw new DomainException(ErrorCodes.MaxBookingPassengers, $"A maximum of {MaxPassengers} passengers can be searched.");

        var currency = (currencyCode ?? session.CurrencyCode ?? "USD").Trim().ToUpperInvariant();
        var includeTaxes = rollup is TaxesAndFeesRollupMode.Taxes or TaxesAndFeesRollupMode.TaxesAndFees;
        var response = new AvailabilityResponse { CurrencyCode = currency, IncludeTaxesAndFees = includeTaxes };

        // 1) Build candidate journeys for every criterion/date.
        var plans = new List<(int Criterion, string Origin, string Destination, DateTime Begin, DateTime End)>();
        var journeys = new List<(int Criterion, DateTime Date, List<InvSegment> Segments)>();
        for (var i = 0; i < criteria.Count; i++)
        {
            var c = criteria[i];
            var origin = Station(c.Stations.OriginStationCodes.FirstOrDefault());
            var destination = Station(c.Stations.DestinationStationCodes.FirstOrDefault());
            if (origin == destination) throw new DomainException(ErrorCodes.InvalidStationCode, "Origin and destination must differ.");

            var begin = c.Dates.BeginDate.Date;
            var end = (c.Dates.EndDate ?? c.Dates.BeginDate).Date;
            if (end < begin || (end - begin).TotalDays > 13)
                throw new DomainException("nsk-server:InvalidDateRange", "The search date range must be 1 to 14 days.");

            plans.Add((i, origin, destination, begin, end));
            foreach (var j in await FindJourneysAsync(origin, destination, begin, end, c.Filters, ct))
                journeys.Add((i, j[0].DepartureLocal.Date, j));
        }

        // 2) Price everything in one call.
        var priced = journeys.Count == 0
            ? new PriceJourneysResponse()
            : await pricing.PriceJourneysAsync(new PriceJourneysRequest
            {
                CurrencyCode = currency,
                PromotionCode = promotionCode,
                Passengers = passengers,
                MaxFaresPerJourney = faresPerJourney is > 0 ? faresPerJourney.Value : DefaultFaresPerJourney,
                Journeys = journeys.Select((j, idx) => new PriceJourneyInput
                {
                    Ref = idx.ToString(CultureInfo.InvariantCulture),
                    Segments = j.Segments.Select((s, si) => new PriceSegmentInput
                    {
                        Ref = si.ToString(CultureInfo.InvariantCulture),
                        CarrierCode = s.CarrierCode,
                        FlightNumber = s.FlightNumber,
                        Origin = s.Origin,
                        Destination = s.Destination,
                        DepartureLocal = s.DepartureLocal,
                        International = s.International,
                        AvailableClasses = s.ClassAvailability
                    }).ToList()
                }).ToList()
            }, ct);
        var pricedByRef = priced.Journeys.ToDictionary(p => p.Ref);

        // 3) Shape the response + remember offers.
        var offerId = Guid.NewGuid().ToString("N");
        var offerEntries = new Dictionary<int, OfferJourneyFare>();
        var fareIndex = 0;

        foreach (var plan in plans)
        {
            var result = new AvailabilityResult();
            var market = $"{plan.Origin}|{plan.Destination}";
            for (var date = plan.Begin; date <= plan.End; date = date.AddDays(1))
            {
                var trip = new AvailabilityTrip { Date = date };
                var list = new List<AvailableJourney>();
                trip.JourneysAvailableByMarket[market] = list;

                for (var idx = 0; idx < journeys.Count; idx++)
                {
                    var (criterion, journeyDate, segments) = journeys[idx];
                    if (criterion != plan.Criterion || journeyDate != date) continue;

                    var journey = BuildJourney(segments);
                    var options = pricedByRef.TryGetValue(idx.ToString(CultureInfo.InvariantCulture), out var pj) ? pj.Options : new List<PricedFareOption>();
                    foreach (var option in options)
                    {
                        var index = fareIndex++;
                        var fak = new FareAvailabilityKey(offerId, index).Encode();
                        journey.Fares.Add(new JourneyFare
                        {
                            FareAvailabilityKey = fak,
                            IsSumOfSector = segments.Count > 1,
                            Details = option.Segments.Select(ps => new JourneyFareDetail { AvailableCount = option.AvailableCount, Reference = ps.FareKey }).ToList()
                        });
                        response.FaresAvailable[fak] = BuildFareAvailability(fak, option, passengers, includeTaxes, segments.Count > 1);
                        offerEntries[index] = new OfferJourneyFare
                        {
                            FareAvailabilityKey = fak,
                            JourneyKey = journey.JourneyKey,
                            FlightType = (int)journey.FlightType,
                            Stops = journey.Stops,
                            CurrencyCode = currency,
                            PromotionCode = promotionCode,
                            Segments = segments.Zip(option.Segments, (s, ps) => new OfferSegment
                            {
                                SegmentKey = s.ToSegmentKey().Encode(),
                                Segment = s,
                                FareId = ps.FareId,
                                FareKey = ps.FareKey,
                                ClassOfService = ps.ClassOfService,
                                ProductClass = ps.ProductClass,
                                TravelClassCode = ps.TravelClassCode
                            }).ToList()
                        };
                    }
                    list.Add(journey);
                }
                result.Trips.Add(trip);
            }
            response.Results.Add(result);
        }

        if (offerEntries.Count > 0) await offers.SaveAsync(offerId, offerEntries);
        return response;
    }

    private async Task<List<List<InvSegment>>> FindJourneysAsync(string origin, string destination, DateTime begin, DateTime end,
        SearchFilters? filters, CancellationToken ct)
    {
        var carrier = string.IsNullOrWhiteSpace(filters?.CarrierCode) ? null : filters!.CarrierCode!.ToUpperInvariant();
        var direct = await inventory.SearchSegmentsAsync(new SegmentSearchQuery
        {
            Origin = origin, Destination = destination, BeginDate = begin, EndDate = end, CarrierCode = carrier
        }, ct);

        var journeys = direct.Select(s => new List<InvSegment> { s }).ToList();
        if (filters?.FlightType is FlightType.NonStop) journeys = journeys.Where(j => j[0].Legs.Count == 1).ToList();

        var maxConnections = filters?.MaxConnections ?? DefaultMaxConnections;
        var allowConnections = maxConnections >= 1 && filters?.FlightType is null or FlightType.All or FlightType.Connect or FlightType.None;
        if (allowConnections)
        {
            var firsts = (await inventory.SearchSegmentsAsync(new SegmentSearchQuery
                {
                    Origin = origin, Destination = null, BeginDate = begin, EndDate = end, CarrierCode = carrier
                }, ct))
                .Where(s => s.Destination != destination && s.Destination != origin)
                .ToList();

            foreach (var hub in firsts.GroupBy(s => s.Destination))
            {
                var seconds = await inventory.SearchSegmentsAsync(new SegmentSearchQuery
                {
                    Origin = hub.Key, Destination = destination, BeginDate = begin, EndDate = end.AddDays(1), CarrierCode = carrier
                }, ct);

                foreach (var first in hub)
                foreach (var second in seconds)
                {
                    var gap = second.DepartureUtc - first.ArrivalUtc;
                    if (gap >= MinConnection && gap <= MaxConnection)
                        journeys.Add(new List<InvSegment> { first, second });
                }
            }
        }

        if (filters?.FlightType is FlightType.Connect) journeys = journeys.Where(j => j.Count > 1).ToList();
        return journeys.OrderBy(j => j[0].DepartureUtc).ThenBy(j => j[^1].ArrivalUtc).ToList();
    }

    private static AvailableJourney BuildJourney(List<InvSegment> segments) => new()
    {
        FlightType = InventoryMapping.FlightTypeFor(segments),
        Stops = InventoryMapping.StopsFor(segments),
        Designator = InventoryMapping.ToDesignator(segments[0].Origin, segments[^1].Destination, segments[0].DepartureLocal, segments[^1].ArrivalLocal),
        JourneyKey = InventoryMapping.JourneyKeyFor(segments),
        Segments = segments.Select(s => new AvailableSegment
        {
            Designator = InventoryMapping.ToDesignator(s.Origin, s.Destination, s.DepartureLocal, s.ArrivalLocal),
            SegmentKey = s.ToSegmentKey().Encode(),
            Identifier = new FlightIdentifier { Identifier = s.FlightNumber, CarrierCode = s.CarrierCode, OpSuffix = s.OpSuffix },
            International = s.International,
            Legs = s.Legs.Select(l => InventoryMapping.ToLeg(s, l)).ToList()
        }).ToList()
    };

    private static FareAvailability BuildFareAvailability(string fak, PricedFareOption option, List<PaxTypeCount> passengers,
        bool includeTaxes, bool sumOfSector)
    {
        int Count(string ptc) => passengers.Where(p => p.Type == ptc).Sum(p => p.Count);

        var fa = new FareAvailability { FareAvailabilityKey = fak, IsSumOfSector = sumOfSector };
        foreach (var ps in option.Segments)
        {
            fa.Fares.Add(new AvailableFare
            {
                FareBasisCode = ps.FareBasisCode,
                ClassOfService = ps.ClassOfService,
                ProductClass = ps.ProductClass,
                RuleNumber = ps.RuleNumber,
                TravelClassCode = ps.TravelClassCode,
                Reference = ps.FareKey,
                PassengerFares = ps.PassengerFares.Select(pf => new AvailablePassengerFare
                {
                    PassengerType = pf.PassengerType,
                    FareAmount = pf.FareAmount,
                    PublishedFare = pf.FareAmount,
                    DiscountedFare = pf.DiscountedFare,
                    RevenueFare = pf.DiscountedFare,
                    ServiceCharges = pf.ServiceCharges,
                    Multiplier = Count(pf.PassengerType)
                }).ToList()
            });

            foreach (var pf in ps.PassengerFares)
            {
                var n = Count(pf.PassengerType);
                fa.Totals.FareTotal += pf.FareAmount * n;
                fa.Totals.PublishedTotal += pf.FareAmount * n;
                var revenue = (pf.DiscountedFare + (includeTaxes ? pf.TaxesAndFees : 0m)) * n;
                fa.Totals.RevenueTotal += revenue;
                fa.Totals.DiscountedTotal += revenue;
            }
        }
        return fa;
    }

    private static string Station(string? code)
    {
        var s = code?.Trim().ToUpperInvariant();
        if (s is null || s.Length != 3 || !s.All(char.IsLetter))
            throw new DomainException(ErrorCodes.InvalidStationCode, $"'{code}' is not a valid station code.");
        return s;
    }
}

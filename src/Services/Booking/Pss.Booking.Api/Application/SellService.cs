using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;
using Pss.BuildingBlocks.Session;
using Pss.Booking.Api.Domain;
using Pss.Booking.Api.Infrastructure;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;
using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.Booking.Api.Application;

/// <summary>Trip sell (blueprint §12.2): resolve offers → re-price for the real passenger mix → hold inventory → build journeys.</summary>
public sealed class SellService(
    IShoppingClient shopping,
    IPricingClient pricing,
    IInventoryClient inventory,
    ISeatClient seatClient,
    IRefDataClient refData,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<SellService> logger)
{
    private int MaxPassengers => configuration.GetValue("Booking:MaxPassengers", 9);
    private string DefaultCurrency => configuration["Booking:DefaultCurrency"] ?? "USD";

    public async Task<BookingModel> SellAsync(DraftContext ctx, TripSellRequest request, CancellationToken ct)
    {
        var session = ctx.Session;
        var draft = ctx.Draft ?? NewDraft(session, (request.CurrencyCode ?? DefaultCurrency).Trim().ToUpperInvariant());

        var currency = (request.CurrencyCode ?? draft.Booking.CurrencyCode).Trim().ToUpperInvariant();
        if (draft.Booking.Journeys.Count > 0 && currency != draft.Booking.CurrencyCode)
            throw new DomainException(ErrorCodes.UnknownSellCurrencyCode, "The currency cannot change once journeys are sold.");
        draft.Booking.CurrencyCode = currency;

        if (request.InfantCount > 0)
            throw new DomainException(ErrorCodes.NotSupportedYet, "Infants are delivered in Phase 2.");

        var types = (request.Passengers?.Types ?? new List<PaxTypeCount>())
            .Where(t => t.Count > 0)
            .GroupBy(t => t.Type.Trim().ToUpperInvariant())
            .Select(g => new PaxTypeCount { Type = g.Key, Count = g.Sum(x => x.Count) })
            .ToList();

        if (draft.Booking.Passengers.Count == 0)
        {
            if (types.Count == 0)
                throw new DomainException(ErrorCodes.NullRequest, "passengers.types is required on the first sell.");
            await ValidatePassengerTypesAsync(types, ct);
            CreatePassengers(draft, types);
        }
        else if (types.Count > 0 && !SameMix(CurrentMix(draft), types))
        {
            // Passenger count is fixed once sold (blueprint §9 invariant 2).
            throw new DomainException(ErrorCodes.PassengerTypesMismatch, "The passenger types must match the passengers already in the booking.");
        }

        if (!string.IsNullOrWhiteSpace(request.PromotionCode))
            draft.Booking.TypeOfSale.PromotionCode = request.PromotionCode.Trim().ToUpperInvariant();

        if (request.Keys.Count > 0)
            await AddJourneysAsync(session, draft, request.Keys, request.PreventOverlap ?? true, ct);

        draft.Booking.Info.PriceStatus = PriceStatus.Valid;
        BookingCalculator.Recalculate(draft.Booking);
        ctx.Set(draft);
        return draft.Booking;
    }

    public BookingDraft NewDraft(SessionInfo session, string currency)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var booking = new BookingModel
        {
            CurrencyCode = currency,
            Info = new BookingInfo
            {
                Status = BookingStatus.Default,
                PriceStatus = PriceStatus.Valid,
                ChannelType = session.ChannelType,
                BookedDate = now,
                ChangeAllowed = true
            },
            Sales = new BookingSales { Created = session.ToPointOfSale(), Source = session.ToSourcePointOfSale() },
            TypeOfSale = new TypeOfSale()
        };
        return new BookingDraft { Booking = booking, CreatedUtc = now };
    }

    private async Task ValidatePassengerTypesAsync(List<PaxTypeCount> types, CancellationToken ct)
    {
        var total = types.Sum(t => t.Count);
        if (total > MaxPassengers)
            throw new DomainException(ErrorCodes.MaxBookingPassengers, $"A booking can have at most {MaxPassengers} passengers.");

        var ptcs = await refData.GetPassengerTypesAsync(ct);
        foreach (var t in types)
        {
            if (t.Type == "INF")
                throw new DomainException(ErrorCodes.InvalidPassengerType, "Infants are sold with infantCount, not as a passenger type.");
            if (!ptcs.TryGetValue(t.Type, out var ptc) || ptc.InActive || !ptc.Allowed)
                throw new DomainException(ErrorCodes.PassengerTypeNotFound, $"Passenger type '{t.Type}' was not found.");
        }
        if (types.All(t => ptcs[t.Type].NonAdult))
            throw new DomainException(ErrorCodes.InvalidPassengerType, "At least one adult passenger is required.");
    }

    private static void CreatePassengers(BookingDraft draft, List<PaxTypeCount> types)
    {
        var number = 0;
        foreach (var t in types.OrderBy(t => t.Type == "ADT" ? 0 : 1))
        {
            for (var i = 0; i < t.Count; i++)
            {
                var key = new PassengerKey(number++, t.Type).Encode();
                draft.Booking.Passengers[key] = new Passenger
                {
                    PassengerKey = key,
                    PassengerTypeCode = t.Type,
                    WeightCategory = t.Type == "CHD" ? WeightCategory.Child : WeightCategory.Unassigned
                };
            }
        }
    }

    public static List<PaxTypeCount> CurrentMix(BookingDraft draft)
        => draft.Booking.Passengers.Values.GroupBy(p => p.PassengerTypeCode)
            .Select(g => new PaxTypeCount { Type = g.Key, Count = g.Count() }).ToList();

    private static bool SameMix(List<PaxTypeCount> a, List<PaxTypeCount> b)
        => a.OrderBy(x => x.Type).Select(x => $"{x.Type}:{x.Count}")
            .SequenceEqual(b.OrderBy(x => x.Type).Select(x => $"{x.Type}:{x.Count}"));

    private async Task AddJourneysAsync(SessionInfo session, BookingDraft draft, List<SellKey> keys, bool preventOverlap, CancellationToken ct)
    {
        // 1) Resolve offers (no side effects).
        var offers = new List<OfferJourneyFare>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key.JourneyKey) || string.IsNullOrWhiteSpace(key.FareAvailabilityKey))
                throw DomainException.InvalidKey(key.JourneyKey);
            JourneyKey.Parse(key.JourneyKey); // validates the key format
            if (draft.Booking.Journeys.Any(j => j.JourneyKey == key.JourneyKey) || offers.Any(o => o.JourneyKey == key.JourneyKey))
                throw new DomainException(ErrorCodes.JourneyAlreadySold, "This journey is already in the booking.");

            var offer = await shopping.GetOfferAsync(key.FareAvailabilityKey, ct);
            if (offer.JourneyKey != key.JourneyKey) throw DomainException.InvalidKey(key.FareAvailabilityKey);
            if (!string.Equals(offer.CurrencyCode, draft.Booking.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                throw new DomainException(ErrorCodes.UnknownSellCurrencyCode, "The fare was shopped in a different currency.");
            offers.Add(offer);
        }

        if (preventOverlap) EnsureNoOverlap(draft.Booking, offers);

        // 2) Re-price for the actual passenger mix.
        var mix = CurrentMix(draft);
        var priced = (await pricing.PriceSelectionAsync(new PriceSelectionRequest
        {
            CurrencyCode = draft.Booking.CurrencyCode,
            PromotionCode = draft.Booking.TypeOfSale.PromotionCode,
            Passengers = mix,
            Segments = offers.SelectMany((o, oi) => o.Segments.Select((s, si) => new PriceSegmentInput
            {
                Ref = $"{oi}:{si}",
                CarrierCode = s.Segment.CarrierCode,
                FlightNumber = s.Segment.FlightNumber,
                Origin = s.Segment.Origin,
                Destination = s.Segment.Destination,
                DepartureLocal = s.Segment.DepartureLocal,
                International = s.Segment.International,
                FareId = s.FareId,
                ClassOfService = s.ClassOfService
            })).ToList()
        }, ct)).Segments.ToDictionary(p => p.SegmentRef);

        // 3) Hold inventory for every leg, all-or-nothing (held until session expiry).
        var seats = mix.Where(p => p.Type != "INF").Sum(p => p.Count);
        var holds = await inventory.HoldAsync(new HoldRequest
        {
            Items = offers.SelectMany(o => o.Segments.SelectMany(s => s.Segment.Legs.Select(l => new HoldItem
            {
                LegId = l.LegId,
                ClassOfService = s.ClassOfService,
                Quantity = seats,
                Reference = o.JourneyKey
            }))).ToList()
        }, ct);

        try
        {
            // 4) Build journeys.
            var now = clock.GetUtcNow().UtcDateTime;
            for (var oi = 0; oi < offers.Count; oi++)
                draft.Booking.Journeys.Add(BuildJourney(offers[oi], oi, priced, draft.Booking.Passengers.Keys, session, now));

            draft.Booking.Journeys = draft.Booking.Journeys.OrderBy(j => j.Designator.Departure).ToList();
            draft.Holds.AddRange(holds.Holds.Select(h => new DraftHold { HoldId = h.HoldId, JourneyKey = h.Reference ?? string.Empty }));
        }
        catch
        {
            await TryReleaseAsync(holds.Holds.Select(h => h.HoldId).ToList(), ct);
            throw;
        }
    }

    private static Journey BuildJourney(OfferJourneyFare offer, int offerIndex, IReadOnlyDictionary<string, PricedSegmentFare> priced,
        IEnumerable<string> passengerKeys, SessionInfo session, DateTime now)
    {
        var paxKeys = passengerKeys.ToList();
        var invSegments = offer.Segments.Select(s => s.Segment).ToList();
        var journey = new Journey
        {
            JourneyKey = offer.JourneyKey,
            FlightType = (FlightType)offer.FlightType,
            Stops = offer.Stops,
            Designator = InventoryMapping.ToDesignator(invSegments[0].Origin, invSegments[^1].Destination,
                invSegments[0].DepartureLocal, invSegments[^1].ArrivalLocal)
        };

        for (var si = 0; si < offer.Segments.Count; si++)
        {
            var os = offer.Segments[si];
            var s = os.Segment;
            var pf = priced[$"{offerIndex}:{si}"];
            journey.Segments.Add(new Segment
            {
                SegmentKey = os.SegmentKey,
                Designator = InventoryMapping.ToDesignator(s.Origin, s.Destination, s.DepartureLocal, s.ArrivalLocal),
                Identifier = new FlightIdentifier { Identifier = s.FlightNumber, CarrierCode = s.CarrierCode, OpSuffix = s.OpSuffix },
                International = s.International,
                CabinOfService = os.TravelClassCode,
                ChannelType = session.ChannelType,
                SalesDate = now,
                Status = SegmentStatus.HeldPending,
                FlightReference = InventoryMapping.FlightReference(s.CarrierCode, s.FlightNumber, s.DepartureLocal, s.Origin, s.Destination),
                Legs = s.Legs.Select(l => InventoryMapping.ToLeg(s, l)).ToList(),
                Fares = new List<SegmentFare>
                {
                    new()
                    {
                        FareKey = pf.FareKey,
                        CarrierCode = pf.CarrierCode,
                        ClassOfService = pf.ClassOfService,
                        FareClassOfService = pf.ClassOfService,
                        OriginalClassOfService = pf.ClassOfService,
                        FareBasisCode = pf.FareBasisCode,
                        FareSequence = pf.FareSequence,
                        RuleNumber = pf.RuleNumber,
                        ProductClass = pf.ProductClass,
                        TravelClassCode = pf.TravelClassCode,
                        PricingDate = now,
                        PassengerFares = pf.PassengerFares.Select(x => new PassengerFare
                        {
                            PassengerType = x.PassengerType,
                            ServiceCharges = x.ServiceCharges
                        }).ToList()
                    }
                },
                PassengerSegment = paxKeys.ToDictionary(k => k, k => new PassengerSegment
                {
                    PassengerKey = k,
                    CreatedDate = now,
                    ActivityDate = now,
                    PriorityDate = now,
                    LiftStatus = LiftStatus.Default
                })
            });
        }
        return journey;
    }

    private static void EnsureNoOverlap(BookingModel booking, List<OfferJourneyFare> offers)
    {
        var intervals = booking.Journeys
            .Select(j => (Start: j.Segments.SelectMany(s => s.Legs).Min(l => l.LegInfo.DepartureTimeUtc),
                          End: j.Segments.SelectMany(s => s.Legs).Max(l => l.LegInfo.ArrivalTimeUtc)))
            .Concat(offers.Select(o => (Start: o.Segments.Min(s => s.Segment.DepartureUtc), End: o.Segments.Max(s => s.Segment.ArrivalUtc))))
            .OrderBy(i => i.Start)
            .ToList();

        for (var i = 1; i < intervals.Count; i++)
            if (intervals[i].Start < intervals[i - 1].End)
                throw new DomainException(ErrorCodes.JourneyOverlap, "Journeys in a booking must not overlap in time.");
    }

    /// <summary>
    /// Releases the session's held (uncommitted) seats in the Seat service — all legs when <paramref name="legIds"/> is null.
    /// Best effort: the Seat service also releases them on SessionExpired and in its sweeper.
    /// </summary>
    public async Task TryReleaseSeatsAsync(IReadOnlyCollection<long>? legIds, CancellationToken ct)
    {
        try
        {
            await seatClient.ReleaseSessionAsync(legIds, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not release held seats; they are released when the session ends");
        }
    }

    public async Task TryReleaseAsync(IReadOnlyCollection<long> holdIds, CancellationToken ct)
    {
        if (holdIds.Count == 0) return;
        try
        {
            await inventory.ReleaseAsync(holdIds, ct);
        }
        catch (Exception ex)
        {
            // Safety net: Inventory sweeps expired holds and releases on SessionExpired.
            logger.LogWarning(ex, "Could not release {Count} holds; they will expire", holdIds.Count);
        }
    }
}

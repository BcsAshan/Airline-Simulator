using Pss.Booking.Api.Domain;
using Pss.BuildingBlocks.Domain;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;
using Pss.Inventory.Api.Domain;
using Pss.Payment.Api.Domain;
using Pss.Pricing.Api.Domain;
using BookingModel = Pss.Contracts.Booking.Booking;
using ContractLeg = Pss.Contracts.Booking.Leg;
using PaymentModel = Pss.Contracts.Booking.Payment;

namespace Pss.UnitTests;

public class InventoryMathTests
{
    [Theory]
    [InlineData(50, 10, 180, 100, 180, 100, 40)] // class AU limits
    [InlineData(50, 10, 180, 175, 180, 175, 5)]  // nest/leg nearly full
    [InlineData(50, 60, 180, 100, 180, 100, 0)]  // oversold class never negative
    public void Class_availability_is_min_of_class_nest_leg(int au, int classSold, int nestLid, int nestSold, int legLid, int legSold, int expected)
        => InventoryMath.ClassAvailable(au, classSold, nestLid, nestSold, legLid, legSold).Should().Be(expected);
}

public class PricingEngineTests
{
    private static PricingSnapshot Snapshot(params Fare[] fares) => new()
    {
        Fares = fares,
        Taxes = new List<TaxDefinition>
        {
            new() { Code = "LKD", Name = "Levy", DepartureCountryCode = "LK", Scope = "INT", ChargeType = 5, Amount = 60, ExemptPassengerTypes = "INF" },
            new() { Code = "YQ", Name = "Fuel", Scope = "ALL", ChargeType = 4, Amount = 25 },
            new() { Code = "VAT", Name = "VAT", DepartureCountryCode = "LK", Scope = "DOM", ChargeType = 5, Amount = 8, IsPercent = true }
        },
        PassengerTypeDiscounts = new Dictionary<string, decimal> { ["ADT"] = 0, ["CHD"] = 25 },
        StationCountries = new Dictionary<string, string> { ["CMB"] = "LK", ["DXB"] = "AE", ["JAF"] = "LK" }
    };

    private static Fare Fare(long id, string cls, decimal amount, string pc, string dest = "DXB") => new()
    {
        FareId = id, CarrierCode = "XX", Origin = "CMB", Destination = dest, ClassOfService = cls, FareBasisCode = cls + pc,
        RuleNumber = pc + "01", ProductClass = pc, Amount = amount, CurrencyCode = "USD"
    };

    [Fact]
    public void Adult_pays_fare_plus_international_taxes()
    {
        var priced = PricingEngine.PriceSegment(Fare(1, "L", 180, "EC"), "0", "CMB", "DXB", true, new[] { "ADT" }, Snapshot());
        var adt = priced.PassengerFares.Single();
        adt.ServiceCharges.Sum(c => c.Amount).Should().Be(180 + 60 + 25);
        adt.ServiceCharges.Should().NotContain(c => c.Code == "VAT");
        adt.TaxesAndFees.Should().Be(85);
    }

    [Fact]
    public void Child_gets_discount_and_domestic_vat_is_percent_of_discounted_fare()
    {
        var priced = PricingEngine.PriceSegment(Fare(2, "L", 100, "EC", "JAF"), "0", "CMB", "JAF", false, new[] { "CHD" }, Snapshot());
        var chd = priced.PassengerFares.Single();
        chd.ServiceCharges.Should().Contain(c => c.Type == ChargeType.Discount && c.Amount == -25);
        chd.ServiceCharges.Should().Contain(c => c.Code == "VAT" && c.Amount == 6);   // 8% of 75
        chd.ServiceCharges.Should().NotContain(c => c.Code == "LKD");                 // domestic
        chd.ServiceCharges.Sum(c => c.Amount).Should().Be(100 - 25 + 6 + 25);
    }

    [Fact]
    public void Journey_pricing_picks_cheapest_class_with_enough_seats_per_product()
    {
        var snapshot = Snapshot(Fare(1, "L", 180, "EC"), Fare(2, "Q", 216, "EC"), Fare(3, "M", 315, "ES"));
        var journey = new PriceJourneyInput
        {
            Ref = "0",
            Segments = { new PriceSegmentInput { Ref = "0", CarrierCode = "XX", Origin = "CMB", Destination = "DXB", International = true,
                AvailableClasses = new Dictionary<string, int> { ["L"] = 1, ["Q"] = 5, ["M"] = 9 } } }
        };
        var pax = new List<PaxTypeCount> { new() { Type = "ADT", Count = 2 } };

        var options = PricingEngine.PriceJourney(journey, pax, snapshot, maxOptions: 5);

        options.Should().HaveCount(2);
        options[0].ProductClass.Should().Be("EC");
        options[0].Segments[0].ClassOfService.Should().Be("Q"); // L has only 1 seat for 2 passengers
        options[0].AvailableCount.Should().Be(5);
        options[1].ProductClass.Should().Be("ES");
    }
}

public class BookingCalculatorTests
{
    private static BookingModel BookingWithTwoAdults()
    {
        var booking = new BookingModel();
        booking.Passengers["MCFBRFQ-"] = new Passenger { PassengerKey = "MCFBRFQ-", PassengerTypeCode = "ADT" };
        booking.Passengers["MSFBRFQ-"] = new Passenger { PassengerKey = "MSFBRFQ-", PassengerTypeCode = "ADT" };
        booking.Journeys.Add(new Journey
        {
            JourneyKey = "J1",
            Segments =
            {
                new Segment
                {
                    Fares =
                    {
                        new SegmentFare
                        {
                            PassengerFares =
                            {
                                new PassengerFare
                                {
                                    PassengerType = "ADT",
                                    ServiceCharges =
                                    {
                                        new ServiceCharge { Type = ChargeType.FarePrice, Amount = 100 },
                                        new ServiceCharge { Type = ChargeType.Tax, Code = "LKD", Amount = 60 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });
        return booking;
    }

    [Fact]
    public void Totals_multiply_passenger_fares_by_passenger_count()
    {
        var booking = BookingWithTwoAdults();
        BookingCalculator.Recalculate(booking);

        booking.Breakdown.TotalAmount.Should().Be(320);
        booking.Breakdown.JourneyTotals.TotalTax.Should().Be(120);
        booking.Breakdown.BalanceDue.Should().Be(320);
        booking.Info.PaidStatus.Should().Be(PaidStatus.UnderPaid);
    }

    [Fact]
    public void Payments_reduce_balance_but_declined_payments_do_not()
    {
        var booking = BookingWithTwoAdults();
        booking.Payments.Add(new PaymentModel { Status = BookingPaymentStatus.Declined, Amounts = new PaymentAmounts { Amount = 320 } });
        BookingCalculator.Recalculate(booking);
        booking.Breakdown.BalanceDue.Should().Be(320);

        booking.Payments.Add(new PaymentModel { Status = BookingPaymentStatus.Received, Amounts = new PaymentAmounts { Amount = 320 } });
        BookingCalculator.Recalculate(booking);
        booking.Breakdown.BalanceDue.Should().Be(0);
        booking.Breakdown.AuthorizedBalanceDue.Should().Be(320);
        booking.Info.PaidStatus.Should().Be(PaidStatus.PaidInFull);
    }
}

public class BookingRuleTests
{
    [Theory]
    [InlineData("Ashan", true)]
    [InlineData("O'Neil-Smith", true)]
    [InlineData("José", true)]
    [InlineData("Bad@Name", false)]
    [InlineData("Semi;colon", false)]
    public void Name_part_validation(string name, bool valid) => NameRules.IsValidPart(name).Should().Be(valid);

    [Fact]
    public void Invalid_first_name_raises_navitaire_code()
    {
        var act = () => NameRules.Validate(new Name { First = "J@ne", Last = "Doe" });
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InvalidFirstNameCharacters);
    }

    [Fact]
    public void Record_locators_use_unambiguous_alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            var rl = RecordLocatorGenerator.Next();
            rl.Should().HaveLength(6);
            rl.Should().NotContainAny("I", "O", "0", "1");
        }
    }

    [Fact]
    public void Age_is_computed_on_travel_date()
    {
        AgeRules.AgeOn(new DateTime(2015, 8, 1), new DateTime(2026, 7, 31)).Should().Be(10);
        AgeRules.AgeOn(new DateTime(2015, 8, 1), new DateTime(2026, 8, 1)).Should().Be(11);
    }

    [Fact]
    public void Hold_not_allowed_close_to_departure()
    {
        var booking = new BookingModel();
        booking.Journeys.Add(new Journey
        {
            Segments = { new Segment { Legs = { new ContractLeg { LegInfo = new LegInfo { DepartureTimeUtc = new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc) } } } } }
        });
        HoldRules.Evaluate(booking, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), 72, 24).Allowed.Should().BeFalse();
        HoldRules.Evaluate(booking, new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), 72, 24).Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("4111abcd11111111", false)]
    public void Card_luhn_validation(string pan, bool valid) => CardRules.IsValidPan(pan).Should().Be(valid);

    [Fact]
    public void Card_expiry_formats()
    {
        CardRules.ParseExpiry("12/2030").Should().Be(new DateTime(2030, 12, 31));
        CardRules.ParseExpiry("2030-02-01T00:00:00").Should().Be(new DateTime(2030, 2, 28));
        CardRules.Mask("4111111111111111").Should().Be("XXXXXXXXXXXX1111");
    }
}

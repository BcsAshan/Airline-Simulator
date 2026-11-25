using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Keys;

namespace Pss.UnitTests;

/// <summary>Keys must round-trip the real Navitaire keys captured in the HAR files (blueprint §10, Phase 0 "done when").</summary>
public class KeyCodecTests
{
    [Fact]
    public void PassengerKey_matches_navitaire()
    {
        KeyCodec.Encode("0!ADT").Should().Be("MCFBRFQ-");
        new PassengerKey(0, "ADT").Encode().Should().Be("MCFBRFQ-");
        PassengerKey.Parse("MCFBRFQ-").Should().Be(new PassengerKey(0, "ADT"));
    }

    [Fact]
    public void JourneyKey_from_har_round_trips()
    {
        const string har = "TlZ_MjU1NX4gfn5TTEN_MDkvMDEvMjAyMyAwMDowMH5KRkt_MDkvMDIvMjAyMyAwMDowMH5_";
        var segments = JourneyKey.Parse(har);

        segments.Should().HaveCount(1);
        var s = segments[0];
        s.CarrierCode.Should().Be("NV");
        s.FlightNumber.Should().Be("2555");
        s.Origin.Should().Be("SLC");
        s.Destination.Should().Be("JFK");
        s.DepartureLocal.Should().Be(new DateTime(2023, 9, 1, 0, 0, 0));
        s.ArrivalLocal.Should().Be(new DateTime(2023, 9, 2, 0, 0, 0));

        JourneyKey.Encode(segments).Should().Be(har);
        s.Encode().Should().Be(har);
    }

    [Fact]
    public void Connection_journey_key_joins_segments_with_caret()
    {
        var a = new SegmentKey("XX", "101", null, "CMB", new DateTime(2026, 12, 1, 8, 30, 0), "DXB", new DateTime(2026, 12, 1, 11, 30, 0));
        var b = new SegmentKey("XX", "701", null, "DXB", new DateTime(2026, 12, 1, 15, 30, 0), "LHR", new DateTime(2026, 12, 1, 19, 20, 0));
        var key = JourneyKey.Encode(new[] { a, b });

        KeyCodec.Decode(key).Should().Contain("^");
        JourneyKey.Parse(key).Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void LegKey_from_har_round_trips()
    {
        const string har = "NjM4MjkxMjMyMDAwMDAwMDAwIU5WITI1NTUhICFTTEMhSkZLITIzOTY3MjM-";
        var leg = LegKey.Parse(har);
        leg.LegId.Should().Be(2396723);
        leg.Origin.Should().Be("SLC");
        leg.Destination.Should().Be("JFK");
        leg.OpSuffix.Should().BeNull();
        leg.Encode().Should().Be(har);
    }

    [Fact]
    public void BookingKey_from_har_round_trips()
    {
        const string har = "MTI2MDkyIUMxS05UWSFmYWxzZQ--";
        var key = BookingKey.Parse(har);
        key.Should().Be(new BookingKey(126092, "C1KNTY", false));
        key.Encode().Should().Be(har);
    }

    [Fact]
    public void PaymentKey_from_har_round_trips()
    {
        PaymentKey.Parse("MTE4NDA3ITEhQ0EhMTI2MDky").Should().Be(new PaymentKey(118407, 1, "CA", 126092));
        new PaymentKey(118407, 1, "CA", 126092).Encode().Should().Be("MTE4NDA3ITEhQ0EhMTI2MDky");
        new PaymentKey(0, 1, "CA", 0).Encode().Should().Be("MCExIUNBITA-"); // draft payment key from the HAR Location header
    }

    [Fact]
    public void SeatmapReference_matches_har()
        => SeatmapReference.Encode("NV", "2555", null, new DateTime(638291232000000000), "SLC", "JFK")
            .Should().Be("TlYhMjU1NSEgITYzODI5MTIzMjAwMDAwMDAwMCFTTEMhSkZL");

    [Fact]
    public void FareAvailabilityKey_round_trips()
    {
        var key = new FareAvailabilityKey("abc123", 7);
        FareAvailabilityKey.Parse(key.Encode()).Should().Be(key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key!")]
    [InlineData("MCFBRFQ")]
    public void Invalid_keys_throw_InvalidKey(string key)
    {
        var act = () => PassengerKey.Parse(key);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InvalidKey);
    }
}

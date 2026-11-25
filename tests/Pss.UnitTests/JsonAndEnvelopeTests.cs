using System.Text.Json;
using Pss.BuildingBlocks.Api;
using Pss.Contracts.Booking;
using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.UnitTests;

public class JsonAndEnvelopeTests
{
    private sealed class Probe
    {
        public Gender Gender { get; set; }
        public Gender? Optional { get; set; }
        public DateTime Date { get; set; }
    }

    [Theory]
    [InlineData("\"Male\"")]
    [InlineData("\"male\"")]
    [InlineData("1")]
    [InlineData("\"1\"")]
    public void Enums_accept_int_or_name(string json)
        => JsonSerializer.Deserialize<Probe>($"{{\"gender\":{json},\"date\":\"2023-9-1\"}}", PssJson.Options)!
            .Gender.Should().Be(Gender.Male);

    [Fact]
    public void Enums_are_written_as_integers_and_dates_without_fraction()
    {
        var json = JsonSerializer.Serialize(new Probe { Gender = Gender.Female, Optional = Gender.Male, Date = new DateTime(2023, 9, 1) }, PssJson.Options);
        json.Should().Contain("\"gender\":2").And.Contain("\"optional\":1").And.Contain("\"date\":\"2023-09-01T00:00:00\"");
    }

    [Fact]
    public void Lenient_dates_are_accepted_and_utc_is_written_with_z()
    {
        var p = JsonSerializer.Deserialize<Probe>("{\"gender\":1,\"date\":\"2023-9-1\"}", PssJson.Options)!;
        p.Date.Should().Be(new DateTime(2023, 9, 1));

        var utc = JsonSerializer.Serialize(new Probe { Date = new DateTime(2023, 8, 9, 20, 28, 14, DateTimeKind.Utc) }, PssJson.Options);
        utc.Should().Contain("\"2023-08-09T20:28:14Z\"");
    }

    [Fact]
    public void Envelope_writes_null_data_and_omits_empty_errors()
    {
        var json = JsonSerializer.Serialize(new ApiResponse<object?> { Data = null }, PssJson.Options);
        json.Should().Be("{\"data\":null}");
    }

    [Fact]
    public void Keyed_collections_keep_their_keys()
    {
        var booking = new BookingModel();
        booking.Passengers["MCFBRFQ-"] = new Passenger { PassengerKey = "MCFBRFQ-" };
        JsonSerializer.Serialize(booking, PssJson.Options).Should().Contain("\"MCFBRFQ-\":{");
    }
}

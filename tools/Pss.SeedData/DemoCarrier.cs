namespace Pss.SeedData;

/// <summary>
/// Demo carrier "XX" (blueprint §25 Phase 0 seed data). Each service seeds only the slice it owns
/// (stations → RefData, flights → Inventory, fares/taxes → Pricing, users → Identity, methods → Payment).
/// Change values here and drop the service databases to re-seed.
/// </summary>
public static class DemoCarrier
{
    public const string CarrierCode = "XX";
    public const string CarrierName = "Demo Air";
    public const string DefaultCurrency = "USD";

    public sealed record Station(string Code, string Name, string CountryCode, string TimeZoneId, string CurrencyCode, string? MacCode = null);
    public sealed record Country(string Code, string Code3, string Name, string CurrencyCode);
    public sealed record Currency(string Code, string Name, string Symbol, int DecimalPlaces);
    public sealed record PassengerType(string Code, string Name, int MinimumAge, int MaximumAge, bool NonAdult, decimal FareDiscountPercent);
    public sealed record Equipment(string Type, string Suffix, string PrbcCode, int BusinessSeats, int EconomySeats);
    public sealed record ScheduledFlight(string FlightNumber, string Origin, string Destination, TimeSpan DepartureLocal, TimeSpan BlockTime, string Equipment);
    public sealed record MarketFare(string Origin, string Destination, decimal EconomyBase);
    public sealed record FareClass(string ClassOfService, string Cabin, int Rank, decimal Multiplier, decimal AuthorizedShare, string ProductClass);
    /// <summary>Scope: "ALL", "INT" (international segments only) or "DOM" (domestic only).</summary>
    public sealed record Tax(string Code, string Name, string? DepartureCountry, string Scope, int ChargeType, decimal Amount, bool IsPercent, string[] ExemptPassengerTypes);
    public sealed record PaymentMethod(string Code, string Name, int Type, bool AgentOnly, bool IsCard);
    public sealed record User(string Domain, string Username, string Password, int UserType, string RoleCode, string OrganizationCode,
        string LocationCode, string Name, bool IsVirtual);
    public sealed record CodeItem(string ListName, string Code, string Name);

    public static readonly Station[] Stations =
    {
        new("CMB", "Colombo Bandaranaike Intl", "LK", "Asia/Colombo", "LKR"),
        new("DXB", "Dubai Intl", "AE", "Asia/Dubai", "AED"),
        new("SIN", "Singapore Changi", "SG", "Asia/Singapore", "SGD"),
        new("BKK", "Bangkok Suvarnabhumi", "TH", "Asia/Bangkok", "THB"),
        new("MLE", "Male Velana Intl", "MV", "Indian/Maldives", "MVR"),
        new("DEL", "Delhi Indira Gandhi Intl", "IN", "Asia/Kolkata", "INR"),
        new("KUL", "Kuala Lumpur Intl", "MY", "Asia/Kuala_Lumpur", "MYR"),
        new("LHR", "London Heathrow", "GB", "Europe/London", "GBP", "LON"),
        new("JAF", "Jaffna Intl", "LK", "Asia/Colombo", "LKR"),
    };

    public static readonly Country[] Countries =
    {
        new("LK", "LKA", "Sri Lanka", "LKR"), new("AE", "ARE", "United Arab Emirates", "AED"),
        new("SG", "SGP", "Singapore", "SGD"), new("TH", "THA", "Thailand", "THB"),
        new("MV", "MDV", "Maldives", "MVR"), new("IN", "IND", "India", "INR"),
        new("MY", "MYS", "Malaysia", "MYR"), new("GB", "GBR", "United Kingdom", "GBP"),
        new("US", "USA", "United States of America", "USD"),
    };

    public static readonly Currency[] Currencies =
    {
        new("USD", "US Dollar", "$", 2), new("LKR", "Sri Lankan Rupee", "Rs", 2), new("AED", "UAE Dirham", "AED", 2),
        new("SGD", "Singapore Dollar", "S$", 2), new("THB", "Thai Baht", "฿", 2), new("MVR", "Maldivian Rufiyaa", "Rf", 2),
        new("INR", "Indian Rupee", "₹", 2), new("MYR", "Malaysian Ringgit", "RM", 2), new("GBP", "Pound Sterling", "£", 2),
        new("EUR", "Euro", "€", 2),
    };

    public static readonly PassengerType[] PassengerTypes =
    {
        new("ADT", "Adult", 12, 120, false, 0m),
        new("CHD", "Child", 2, 11, true, 25m),
        new("INF", "Infant", 0, 1, true, 90m),
    };

    public static readonly Equipment[] Equipments =
    {
        new("320", "", "A320XX", 0, 180),
        new("321", "", "A321XX", 12, 176),
        new("333", "", "A333XX", 28, 269),
        new("AT7", "", "ATR72XX", 0, 70),
    };

    /// <summary>Daily schedule (station-local departure times). Hub: CMB; DXB is a connection point for LHR.</summary>
    public static readonly ScheduledFlight[] Schedule =
    {
        new("101", "CMB", "DXB", new(8, 30, 0), new(4, 30, 0), "321"),
        new("102", "DXB", "CMB", new(14, 0, 0), new(4, 15, 0), "321"),
        new("201", "CMB", "SIN", new(23, 55, 0), new(3, 45, 0), "320"),
        new("202", "SIN", "CMB", new(8, 0, 0), new(3, 40, 0), "320"),
        new("301", "CMB", "BKK", new(1, 10, 0), new(3, 15, 0), "320"),
        new("302", "BKK", "CMB", new(7, 30, 0), new(3, 20, 0), "320"),
        new("401", "CMB", "MLE", new(10, 0, 0), new(1, 20, 0), "320"),
        new("402", "MLE", "CMB", new(12, 30, 0), new(1, 20, 0), "320"),
        new("501", "CMB", "DEL", new(9, 15, 0), new(3, 45, 0), "321"),
        new("502", "DEL", "CMB", new(14, 30, 0), new(3, 50, 0), "321"),
        new("601", "CMB", "KUL", new(13, 0, 0), new(3, 50, 0), "320"),
        new("602", "KUL", "CMB", new(18, 30, 0), new(3, 50, 0), "320"),
        new("701", "DXB", "LHR", new(15, 30, 0), new(7, 50, 0), "333"),
        new("702", "LHR", "DXB", new(21, 0, 0), new(6, 55, 0), "333"),
        new("801", "CMB", "JAF", new(7, 0, 0), new(1, 0, 0), "AT7"),
        new("802", "JAF", "CMB", new(9, 0, 0), new(1, 0, 0), "AT7"),
    };

    /// <summary>Economy base (lowest class) one-way fare per market, USD. Reverse markets use the same base.</summary>
    public static readonly MarketFare[] MarketFares =
    {
        new("CMB", "DXB", 180m), new("CMB", "SIN", 160m), new("CMB", "BKK", 150m), new("CMB", "MLE", 90m),
        new("CMB", "DEL", 140m), new("CMB", "KUL", 150m), new("DXB", "LHR", 420m), new("CMB", "JAF", 45m),
    };

    /// <summary>Booking classes. Cabin C (business) and Y (economy); AuthorizedShare = AU as share of the cabin.</summary>
    public static readonly FareClass[] FareClasses =
    {
        new("J", "C", 1, 6.0m, 1.00m, "BZ"), new("C", "C", 2, 5.0m, 0.70m, "BZ"), new("D", "C", 3, 4.0m, 0.40m, "BZ"),
        new("Y", "Y", 1, 2.6m, 1.00m, "EF"), new("B", "Y", 2, 2.1m, 0.80m, "EF"),
        new("M", "Y", 3, 1.75m, 0.60m, "ES"), new("H", "Y", 4, 1.45m, 0.45m, "ES"),
        new("Q", "Y", 5, 1.2m, 0.30m, "EC"), new("L", "Y", 6, 1.0m, 0.15m, "EC"),
    };

    /// <summary>ChargeType: 5 = Tax, 4 = TravelFee (blueprint §11).</summary>
    public static readonly Tax[] Taxes =
    {
        new("LKD", "Sri Lanka Embarkation Levy", "LK", "INT", 5, 60m, false, new[] { "INF" }),
        new("AEP", "UAE Passenger Service Charge", "AE", "INT", 5, 20m, false, new[] { "INF" }),
        new("GBA", "UK Air Passenger Duty", "GB", "INT", 5, 95m, false, new[] { "INF", "CHD" }),
        new("YQ", "Fuel Surcharge", null, "ALL", 4, 25m, false, Array.Empty<string>()),
        new("VAT", "Domestic VAT", "LK", "DOM", 5, 8m, true, Array.Empty<string>()),
    };

    public static readonly PaymentMethod[] PaymentMethods =
    {
        new("CA", "Cash", 1, AgentOnly: true, IsCard: false),
        new("VI", "Visa", 0, AgentOnly: false, IsCard: true),
        new("MC", "MasterCard", 0, AgentOnly: false, IsCard: true),
    };

    /// <summary>UserType: 1 Customer, 2 Agent, 3 Anonymous. Anonymous users are virtual (no interactive logon).</summary>
    public static readonly User[] Users =
    {
        new("WW2", "ApiAnonymous", "", 3, "WWWA", "SYSTEM", "WWW", "Anonymous API user", IsVirtual: true),
        new("DEF", "agent01", "Password1!", 2, "AGNT", "XX", "CMB", "Demo Agent", IsVirtual: false),
        new("WWW", "customer@demo.test", "Password1!", 1, "WWWM", "SYSTEM", "WWW", "Demo Customer", IsVirtual: false),
    };

    public static readonly CodeItem[] CodeLists =
    {
        new("titles", "MR", "Mr"), new("titles", "MRS", "Mrs"), new("titles", "MS", "Ms"), new("titles", "MSTR", "Master"), new("titles", "MISS", "Miss"),
        new("documentTypes", "P", "Passport"), new("documentTypes", "NIC", "National Identity Card"), new("documentTypes", "V", "Visa"),
        new("contactTypes", "P", "Primary"), new("contactTypes", "H", "Home"), new("contactTypes", "E", "Emergency"),
        new("nonBinaryGenders", "M", "Male"), new("nonBinaryGenders", "F", "Female"), new("nonBinaryGenders", "X", "Unspecified"),
    };

    /// <summary>Settings documents served from /api/nsk/v1/settings/{name} (JSON).</summary>
    public static readonly (string Name, string Json)[] Settings =
    {
        ("booking/passenger", """{"requiredFields":["first","last"],"maxPassengers":9,"suppressAgeValidation":false}"""),
        ("booking/contact", """{"requiredFields":["first","last","emailAddress"],"primaryContactType":"P"}"""),
        ("booking/general", """{"defaultCurrencyCode":"USD","holdAllowedHoursBeforeDeparture":72,"maxHoldHours":24}"""),
        ("payment", """{"allowOverpayment":false,"allowedCurrencies":["USD"]}"""),
        ("general", """{"carrierCode":"XX","carrierName":"Demo Air","sessionIdleTimeoutMinutes":15}"""),
    };

    // ------------------------------------------------------------------ seats (Phase 2, Seat service)

    /// <summary>
    /// One cabin of a seat map. <paramref name="Columns"/> lists seat letters in seat groups separated by aisles,
    /// e.g. "ABC DEF" (3-3) or "A DG K" (1-2-1). <paramref name="PreferredRows"/> = first N rows sold as "preferred".
    /// <paramref name="Blocked"/> seats exist physically but are never assignable (crew rest, galley, etc.).
    /// </summary>
    public sealed record CabinLayout(string Compartment, int FirstRow, int LastRow, string Columns, int[] ExitRows, int PreferredRows, string[] Blocked);

    /// <summary>Seat map per equipment type. Assignable seats per cabin equal the Equipment capacity above.</summary>
    public sealed record SeatLayout(string EquipmentType, string Name, CabinLayout[] Cabins);

    public static readonly SeatLayout[] SeatLayouts =
    {
        // A320: 30 × 6 = 180 Y
        new("320", "Airbus A320", new[]
        {
            new CabinLayout("Y", 1, 30, "ABC DEF", new[] { 12, 13 }, 5, Array.Empty<string>()),
        }),
        // A321: 3 × 4 = 12 C; 30 × 6 = 180 Y − 4 blocked = 176 Y
        new("321", "Airbus A321", new[]
        {
            new CabinLayout("C", 1, 3, "AC DF", Array.Empty<int>(), 0, Array.Empty<string>()),
            new CabinLayout("Y", 4, 33, "ABC DEF", new[] { 16, 17 }, 5, new[] { "33A", "33B", "33E", "33F" }),
        }),
        // A330-300: 7 × 4 = 28 C (1-2-1); 30 × 9 = 270 Y − 1 blocked = 269 Y (3-3-3)
        new("333", "Airbus A330-300", new[]
        {
            new CabinLayout("C", 1, 7, "A DG K", Array.Empty<int>(), 0, Array.Empty<string>()),
            new CabinLayout("Y", 10, 39, "ABC DEG HJK", new[] { 24 }, 5, new[] { "39K" }),
        }),
        // ATR 72: 18 × 4 = 72 Y − 2 blocked = 70 Y
        new("AT7", "ATR 72", new[]
        {
            new CabinLayout("Y", 1, 18, "AC DF", new[] { 12 }, 3, new[] { "1A", "1C" }),
        }),
    };

    /// <summary>Seat fee groups (per passenger, per flight leg, USD).</summary>
    public sealed record SeatFeeGroup(int Group, string Name, decimal Amount);

    public static readonly SeatFeeGroup[] SeatFeeGroups =
    {
        new(1, "Standard", 8m),
        new(2, "Preferred", 15m),
        new(3, "Extra legroom", 25m),
        new(4, "Business", 0m),
    };

    /// <summary>Fare product classes whose seats are free (Flex economy and Business).</summary>
    public static readonly string[] SeatFeeWaivedProductClasses = { "EF", "BZ" };

    /// <summary>Passenger types that may not sit in an emergency exit row.</summary>
    public static readonly string[] ExitRowRestrictedPassengerTypes = { "CHD", "INF" };

    public static TimeZoneInfo TimeZoneFor(string stationCode)
    {
        var st = Stations.FirstOrDefault(s => s.Code == stationCode)
                 ?? throw new ArgumentException($"Unknown station {stationCode}");
        return TimeZoneInfo.FindSystemTimeZoneById(st.TimeZoneId);
    }

    public static string CountryOf(string stationCode)
        => Stations.FirstOrDefault(s => s.Code == stationCode)?.CountryCode ?? string.Empty;
}

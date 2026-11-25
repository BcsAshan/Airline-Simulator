namespace Pss.RefData.Api.Domain;

public class Station
{
    public string StationCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string? MacCode { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public string CurrencyCode { get; set; } = "USD";
    public bool InActive { get; set; }
}

public class Country
{
    public string CountryCode { get; set; } = string.Empty;
    public string CountryCode3C { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultCurrencyCode { get; set; } = string.Empty;
    public bool InActive { get; set; }
}

public class Currency
{
    public string CurrencyCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; } = 2;
    public bool InActive { get; set; }
}

public class Market
{
    public int Id { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string TravelLocationCode { get; set; } = string.Empty;
    public bool InActive { get; set; }
}

public class PassengerType
{
    public string PassengerTypeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MinimumAge { get; set; }
    public int MaximumAge { get; set; }
    public bool NonAdult { get; set; }
    public bool Allowed { get; set; } = true;
    public bool InActive { get; set; }
}

/// <summary>Simple code lists: titles, documentTypes, contactTypes, nonBinaryGenders …</summary>
public class CodeListItem
{
    public int Id { get; set; }
    public string ListName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool InActive { get; set; }
}

/// <summary>Settings documents (booking/passenger, booking/contact, payment …) stored as JSON.</summary>
public class SettingDocument
{
    public string Name { get; set; } = string.Empty;
    public string Json { get; set; } = "{}";
}

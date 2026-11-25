namespace Pss.Contracts.Booking;

public class Name
{
    public string? First { get; set; }
    public string? Middle { get; set; }
    public string? Last { get; set; }
    public string? Title { get; set; }
    public string? Suffix { get; set; }
}

public class Passenger
{
    public string PassengerKey { get; set; } = string.Empty;
    public string? PassengerAlternateKey { get; set; }
    public string? CustomerNumber { get; set; }
    public List<PassengerFee> Fees { get; set; } = new();
    public Name Name { get; set; } = new();
    public string PassengerTypeCode { get; set; } = "ADT";
    public string? DiscountCode { get; set; }
    public List<object> Bags { get; set; } = new();
    public PassengerProgram? Program { get; set; }
    public PassengerInfant? Infant { get; set; }
    public PassengerInfo Info { get; set; } = new();
    public List<TravelDocument> TravelDocuments { get; set; } = new();
    public List<object> Addresses { get; set; } = new();
    public WeightCategory WeightCategory { get; set; }
    public List<object> EmdCoupons { get; set; } = new();
}

public class PassengerInfo
{
    public string? Nationality { get; set; }
    public string? ResidentCountry { get; set; }
    public Gender? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? FamilyNumber { get; set; }
    public string? DeclaredGender { get; set; }
}

public class PassengerProgram
{
    public string? Code { get; set; }
    public string? LevelCode { get; set; }
    public string? Number { get; set; }
}

public class PassengerInfant
{
    public Name? Name { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string? Nationality { get; set; }
    public string? ResidentCountry { get; set; }
    public List<TravelDocument> TravelDocuments { get; set; } = new();
    public List<PassengerFee> Fees { get; set; } = new();
}

public class TravelDocument
{
    public string? PassengerTravelDocumentKey { get; set; }
    public string? DocumentTypeCode { get; set; }
    public string? Number { get; set; }
    public string? IssuedByCode { get; set; }
    public string? Nationality { get; set; }
    public string? BirthCountry { get; set; }
    public Name? Name { get; set; }
    public Gender? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime? IssuedDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
}

public class PassengerFee
{
    public string? PassengerFeeKey { get; set; }
    public FeeType Type { get; set; }
    public string? Code { get; set; }
    public string? Detail { get; set; }
    public string? SsrCode { get; set; }
    public int? SsrNumber { get; set; }
    public int? PaymentNumber { get; set; }
    public bool IsConfirmed { get; set; }
    public bool IsConfirming { get; set; }
    public bool Override { get; set; }
    public bool IsProtected { get; set; }
    public string? FlightReference { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedDate { get; set; }
    public List<ServiceCharge> ServiceCharges { get; set; } = new();
}

public class Contact
{
    public string ContactTypeCode { get; set; } = "P";
    public List<PhoneNumber> PhoneNumbers { get; set; } = new();
    public string? CultureCode { get; set; }
    public Address? Address { get; set; }
    public string? EmailAddress { get; set; }
    public string? CustomerNumber { get; set; }
    public string? SourceOrganization { get; set; }
    public DistributionOption DistributionOption { get; set; } = DistributionOption.Email;
    public int NotificationPreference { get; set; }
    public string? CompanyName { get; set; }
    public Name? Name { get; set; }
}

public class PhoneNumber
{
    public PhoneNumberType Type { get; set; }
    public string? Number { get; set; }
}

public class Address
{
    public string? LineOne { get; set; }
    public string? LineTwo { get; set; }
    public string? LineThree { get; set; }
    public string? CountryCode { get; set; }
    public string? ProvinceState { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
}

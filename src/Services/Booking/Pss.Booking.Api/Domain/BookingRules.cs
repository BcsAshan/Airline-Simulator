using System.Net.Mail;
using System.Security.Cryptography;
using Pss.BuildingBlocks.Domain;
using Pss.Contracts.Booking;
using BookingModel = Pss.Contracts.Booking.Booking;

namespace Pss.Booking.Api.Domain;

/// <summary>Name rules (blueprint §13.2, 4.5.3+): no special characters, ≤ 32 chars per part, title ≤ 6.</summary>
public static class NameRules
{
    private const string InvalidCharacters = "~`!@#$%^&*()_+=[]\\|;:\",<>/?{}";

    public static void Validate(Name? name)
    {
        if (name is null) return;
        Check(name.First, ErrorCodes.InvalidFirstNameCharacters, "first");
        Check(name.Middle, ErrorCodes.InvalidMiddleNameCharacters, "middle");
        Check(name.Last, ErrorCodes.InvalidLastNameCharacters, "last");
        if (name.Title is { Length: > 6 })
            throw new DomainException("nsk:Validation:InvalidTitle", "Title must be 6 characters or fewer.");
        if (name.Suffix is { Length: > 6 })
            throw new DomainException("nsk:Validation:InvalidSuffix", "Suffix must be 6 characters or fewer.");
    }

    public static bool IsValidPart(string? value)
        => value is null || (value.Length <= 32 && value.IndexOfAny(InvalidCharacters.ToCharArray()) < 0);

    private static void Check(string? value, string code, string part)
    {
        if (!IsValidPart(value))
            throw new DomainException(code, $"The {part} name contains invalid characters or is longer than 32 characters.");
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ContactRules
{
    public static void Validate(Contact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.ContactTypeCode) || contact.ContactTypeCode.Trim().Length != 1)
            throw new DomainException("nsk:Contact:InvalidContactTypeCode", "contactTypeCode must be a single character.");
        NameRules.Validate(contact.Name);
        if (!string.IsNullOrWhiteSpace(contact.EmailAddress) && !MailAddress.TryCreate(contact.EmailAddress.Trim(), out _))
            throw new DomainException("nsk:Contact:InvalidEmailAddress", "The email address is not valid.");
    }

    /// <summary>"primary" is an alias of the primary contact type "P".</summary>
    public static string NormalizeType(string code)
        => code.Equals("primary", StringComparison.OrdinalIgnoreCase) ? "P" : code.Trim().ToUpperInvariant();
}

/// <summary>6-character record locator from an unambiguous alphabet (no I/O/0/1), blueprint §12.3.</summary>
public static class RecordLocatorGenerator
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Next()
    {
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}

/// <summary>Hold eligibility (GET booking/hold/available, blueprint §13.11).</summary>
public static class HoldRules
{
    public static DateTime? FirstDepartureUtc(BookingModel booking)
        => booking.Journeys.SelectMany(j => j.Segments).SelectMany(s => s.Legs)
            .Select(l => (DateTime?)DateTime.SpecifyKind(l.LegInfo.DepartureTimeUtc, DateTimeKind.Utc)).Min();

    /// <summary>Hold allowed when first departure is at least <paramref name="minHoursBeforeDeparture"/> away; expires after maxHoldHours.</summary>
    public static (bool Allowed, DateTime? Expiration) Evaluate(BookingModel booking, DateTime nowUtc, int minHoursBeforeDeparture, int maxHoldHours)
    {
        var first = FirstDepartureUtc(booking);
        if (first is null || first.Value - nowUtc < TimeSpan.FromHours(minHoursBeforeDeparture)) return (false, null);
        var expiration = nowUtc.AddHours(maxHoldHours);
        var latest = first.Value.AddHours(-minHoursBeforeDeparture / 2.0);
        return (true, expiration < latest ? expiration : latest);
    }
}

/// <summary>Age of a passenger on a given date (for PTC age-range validation, blueprint §9 invariant 3).</summary>
public static class AgeRules
{
    public static int AgeOn(DateTime dateOfBirth, DateTime onDate)
    {
        var age = onDate.Year - dateOfBirth.Year;
        if (onDate.Date < dateOfBirth.Date.AddYears(age)) age--;
        return age;
    }
}

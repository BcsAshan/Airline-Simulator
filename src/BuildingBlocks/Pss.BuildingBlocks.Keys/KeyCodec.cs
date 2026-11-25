using System.Text;
using Pss.BuildingBlocks.Domain;

namespace Pss.BuildingBlocks.Keys;

/// <summary>
/// Navitaire-compatible opaque keys (blueprint §10): Base64(UTF8(parts joined by '!' or '~')),
/// then '+' -> '_', '/' -> '~', '=' -> '-'. Verified against HAR keys, e.g. "0!ADT" == "MCFBRFQ-".
/// </summary>
public static class KeyCodec
{
    public static string Encode(string raw)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return b64.Replace('+', '_').Replace('/', '~').Replace('=', '-');
    }

    public static string Encode(char separator, params object?[] parts)
        => Encode(string.Join(separator, parts.Select(p => p switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => p.ToString() ?? string.Empty
        })));

    public static bool TryDecode(string? key, out string raw)
    {
        raw = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var b64 = key.Trim().Replace('_', '+').Replace('~', '/').Replace('-', '=');
        if (b64.Length % 4 != 0) return false;
        try
        {
            raw = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Decodes a key or throws nsk:Exceptions:InvalidKey (400).</summary>
    public static string Decode(string? key)
        => TryDecode(key, out var raw) ? raw : throw DomainException.InvalidKey(key);

    /// <summary>Decodes and splits a key, validating the minimum number of parts.</summary>
    public static string[] DecodeParts(string? key, char separator, int minParts)
    {
        var parts = Decode(key).Split(separator);
        if (parts.Length < minParts) throw DomainException.InvalidKey(key);
        return parts;
    }
}

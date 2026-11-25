namespace Pss.BuildingBlocks.Domain;

/// <summary>
/// A business/validation failure that maps to a Navitaire-style error envelope (blueprint §8.3, §21).
/// Throw it from anywhere; the API exception middleware converts it to { data:null, errors:[...] }.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message, int httpStatus = 400, object? details = null, string errorType = ErrorTypes.Validation)
        : base(message)
    {
        Code = code;
        HttpStatus = httpStatus;
        Details = details;
        ErrorType = errorType;
    }

    public string Code { get; }
    public int HttpStatus { get; }
    public object? Details { get; }
    public string ErrorType { get; }

    public static DomainException Unauthorized(string message = "No valid session/token was supplied.", string code = ErrorCodes.Unauthorized)
        => new(code, message, 401, null, ErrorTypes.Error);

    public static DomainException AgentOnly()
        => new(ErrorCodes.AgentOnly, "This operation requires an agent session.", 401, null, ErrorTypes.Error);

    public static DomainException NotFound(string code, string message)
        => new(code, message, 404, null, ErrorTypes.Error);

    public static DomainException Conflict(string code, string message)
        => new(code, message, 409, null, ErrorTypes.Error);

    public static DomainException InvalidKey(string? key)
        => new(ErrorCodes.InvalidKey, $"The key '{key}' is not valid.", 400, null, ErrorTypes.Validation);
}

public static class ErrorTypes
{
    public const string Validation = "Validation";
    public const string Error = "Error";
    public const string BadRequest = "BadRequest";
    public const string Information = "Information";
}

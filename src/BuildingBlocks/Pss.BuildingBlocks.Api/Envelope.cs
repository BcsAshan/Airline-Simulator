using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Pss.BuildingBlocks.Api;

/// <summary>Navitaire response envelope: { data, errors?, messages? } (blueprint §8.3).</summary>
public sealed class ApiResponse<T>
{
    public T? Data { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ApiError>? Errors { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ApiMessage>? Messages { get; set; }
}

public sealed class ApiError
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Debug mode only (never in production).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawMessage { get; set; }

    public string Type { get; set; } = "Validation";
    public object? Details { get; set; }
}

public sealed class ApiMessage
{
    public string Code { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawValue { get; set; }

    public string Type { get; set; } = "Information";
    public int Status { get; set; }
    public object? Details { get; set; }

    public static ApiMessage Warning(string code, string value) => new() { Code = code, Value = value, Type = "Warning" };
    public static ApiMessage Info(string code, string value) => new() { Code = code, Value = value, Type = "Information" };
}

/// <summary>Result helpers that always wrap payloads in the envelope.</summary>
public static class Envelope
{
    public static IResult Ok<T>(T data, List<ApiMessage>? messages = null)
        => Results.Json(new ApiResponse<T> { Data = data, Messages = messages is { Count: > 0 } ? messages : null }, PssJson.Options, statusCode: 200);

    /// <summary>200 with { "data": null } — used by command endpoints.</summary>
    public static IResult OkNoData(List<ApiMessage>? messages = null)
        => Results.Json(new ApiResponse<object?> { Data = null, Messages = messages is { Count: > 0 } ? messages : null }, PssJson.Options, statusCode: 200);

    /// <summary>201 with Location header and { "data": null } (or data for token creation).</summary>
    public static IResult Created(string? location, object? data = null)
        => new CreatedEnvelopeResult(location, data);

    public static IResult NotFound()
        => Results.Json(new ApiResponse<object?> { Data = null }, PssJson.Options, statusCode: 404);

    private sealed class CreatedEnvelopeResult(string? location, object? data) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            if (!string.IsNullOrEmpty(location)) httpContext.Response.Headers.Location = location;
            return Results.Json(new ApiResponse<object?> { Data = data }, PssJson.Options, statusCode: 201).ExecuteAsync(httpContext);
        }
    }
}

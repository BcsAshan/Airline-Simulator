using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Pss.BuildingBlocks.Api;

/// <summary>Strong ETag (base64 SHA-1 of payload) + If-None-Match → 304, for /resources and /settings (blueprint §7.2).</summary>
public static class ETagResults
{
    public static IResult OkWithETag<T>(HttpContext context, T data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ApiResponse<T> { Data = data }, PssJson.Options);
        var etag = "\"" + Convert.ToBase64String(SHA1.HashData(bytes)) + "\"";
        context.Response.Headers.ETag = etag;

        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) &&
            ifNoneMatch.Split(',').Select(v => v.Trim()).Any(v => v == etag || v == "*"))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(bytes, "application/json; charset=utf-8");
    }
}

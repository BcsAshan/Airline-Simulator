using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Pss.BuildingBlocks.Domain;

namespace Pss.BuildingBlocks.Api;

/// <summary>
/// Helpers for service-to-service calls. Downstream envelope errors are re-thrown as DomainException
/// with the same code/status so they surface unchanged to the client.
/// </summary>
public static class InternalHttp
{
    public static Task<HttpResponseMessage> PostEnvelopeAsync<TBody>(this HttpClient client, string uri, TBody body, CancellationToken ct = default)
        => client.PostAsJsonAsync(uri, body, PssJson.Options, ct);

    public static async Task<T> ReadDataAsync<T>(this HttpResponseMessage response, CancellationToken ct = default)
    {
        if (!response.IsSuccessStatusCode) await ThrowFromErrorAsync(response, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(PssJson.Options, ct);
        if (envelope is null || envelope.Data is null)
            throw new DomainException(ErrorCodes.ServiceUnavailable,
                $"Downstream service returned no data ({response.RequestMessage?.RequestUri?.AbsolutePath}).", 502, null, ErrorTypes.Error);
        return envelope.Data;
    }

    public static async Task EnsureEnvelopeSuccessAsync(this HttpResponseMessage response, CancellationToken ct = default)
    {
        if (!response.IsSuccessStatusCode) await ThrowFromErrorAsync(response, ct);
    }

    private static async Task ThrowFromErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ApiResponse<JsonElement?>? envelope = null;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement?>>(PssJson.Options, ct);
        }
        catch (JsonException) { }
        catch (NotSupportedException) { }

        var error = envelope?.Errors?.FirstOrDefault();
        if (error is not null)
            throw new DomainException(error.Code, error.Message, (int)response.StatusCode, error.Details, error.Type);

        var status = response.StatusCode == HttpStatusCode.NotFound ? 404 : 502;
        throw new DomainException(ErrorCodes.ServiceUnavailable,
            $"Downstream call {response.RequestMessage?.RequestUri?.AbsolutePath} failed with HTTP {(int)response.StatusCode}.",
            status, null, ErrorTypes.Error);
    }
}

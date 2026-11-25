using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Shopping.Api.Application;
using Pss.Shopping.Api.Infrastructure;
using Pss.Shopping.Api.Models;

namespace Pss.Shopping.Api.Endpoints;

public static class AvailabilityEndpoints
{
    public static WebApplication MapAvailabilityEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/nsk").WithTags("Availability").RequirePermission(PermissionLevel.Anonymous);

        g.MapPost("/v4/availability/search/simple", async (SimpleSearchRequest? request, HttpContext http, AvailabilityService svc, CancellationToken ct) =>
        {
            if (request is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
            return Envelope.Ok(await svc.SearchSimpleAsync(request, http.GetSession(), ct));
        });

        g.MapPost("/v4/availability/search", async (FullSearchRequest? request, HttpContext http, AvailabilityService svc, CancellationToken ct) =>
        {
            if (request is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
            return Envelope.Ok(await svc.SearchFullAsync(request, http.GetSession(), ct));
        });

        // Internal: Booking resolves fareAvailabilityKey → offer at trip sell.
        app.MapGroup("/internal/shopping").WithTags("Internal").RequirePermission(PermissionLevel.Anonymous)
            .MapGet("/offers/{fareAvailabilityKey}", async (string fareAvailabilityKey, OfferStore offers)
                => Envelope.Ok(await offers.GetAsync(fareAvailabilityKey)));

        return app;
    }
}

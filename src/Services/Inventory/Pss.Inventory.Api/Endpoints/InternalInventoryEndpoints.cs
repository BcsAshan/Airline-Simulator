using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Session;
using Pss.Contracts.Booking;
using Pss.Contracts.Internal;
using Pss.Inventory.Api.Application;
using Pss.Inventory.Api.Domain;

namespace Pss.Inventory.Api.Endpoints;

/// <summary>Internal API (not routed by the gateway). Callers forward the user's token; holds are bound to its session.</summary>
public static class InternalInventoryEndpoints
{
    public static WebApplication MapInternalInventoryEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/internal/inventory").WithTags("Internal").RequirePermission(PermissionLevel.Anonymous);

        g.MapPost("/segments/search", async (SegmentSearchQuery query, InventoryService inventory, CancellationToken ct)
            => Envelope.Ok(await inventory.SearchSegmentsAsync(query, ct)));

        g.MapPost("/holds", async (HoldRequest request, HttpContext http, InventoryService inventory, CancellationToken ct)
            => Envelope.Ok(await inventory.HoldAsync(http.GetSession().SessionId, request, ct)));

        g.MapPost("/holds/confirm", async (ConfirmHoldsRequest request, HttpContext http, InventoryService inventory, CancellationToken ct)
            => Envelope.Ok(new { confirmed = await inventory.ConfirmAsync(http.GetSession().SessionId, request, ct) }));

        g.MapPost("/holds/release", async (ReleaseHoldsRequest request, HttpContext http, InventoryService inventory, CancellationToken ct)
            => Envelope.Ok(new { released = await inventory.ReleaseAsync(http.GetSession().SessionId, request.HoldIds, HoldStatus.Released, ct) }));

        return app;
    }
}

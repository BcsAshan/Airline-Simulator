using Pss.BuildingBlocks.Api;
using Pss.BuildingBlocks.Domain;
using Pss.BuildingBlocks.Session;
using Pss.Booking.Api.Application;
using Pss.Booking.Api.Domain;
using Pss.Booking.Api.Infrastructure;
using Pss.Contracts.Booking;

namespace Pss.Booking.Api.Endpoints;

/// <summary>
/// Public Booking API (blueprint §7.6). Singular /booking/... = stateful (booking in state);
/// plural /bookings/... = stateless by record locator.
/// </summary>
public static class BookingEndpoints
{
    public static WebApplication MapBookingEndpoints(this WebApplication app)
    {
        var nsk = app.MapGroup("/api/nsk").WithTags("Booking").RequirePermission(PermissionLevel.Anonymous);

        // ---------------------------------------------------------------- trip
        nsk.MapPost("/v4/trip/sell", async (TripSellRequest? request, HttpContext http, StatefulBookingExecutor exec, SellService sell, CancellationToken ct) =>
        {
            if (request is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
            return Envelope.Ok(await exec.ExecuteAsync(http.GetSession(), c => sell.SellAsync(c, request, ct), ct));
        }).WithTags("Trip");

        foreach (var v in new[] { "v4", "v5" })
        {
            nsk.MapPost($"/{v}/trip", async (TripRequest? request, HttpContext http, StatefulBookingExecutor exec, TripService trip, CancellationToken ct) =>
            {
                if (request is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
                return Envelope.Ok(await exec.ExecuteAsync(http.GetSession(), c => trip.CreateAsync(c, request, ct), ct));
            }).WithTags("Trip");
        }

        // ---------------------------------------------------------------- booking in state
        nsk.MapGet("/v1/booking", async (HttpContext http, DraftStore drafts)
            => Envelope.Ok((await RequireDraftAsync(http, drafts)).Booking));

        nsk.MapGet("/v2/booking/status", async (HttpContext http, DraftStore drafts)
            => Envelope.Ok((await RequireDraftAsync(http, drafts)).Booking));

        nsk.MapDelete("/v1/booking/reset", async (HttpContext http, StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct) =>
        {
            await exec.ExecuteAsync(http.GetSession(), async c => { await cmd.ResetAsync(c, ct); return true; }, ct);
            return Envelope.OkNoData();
        });

        nsk.MapPost("/v3/booking", (CommitRequest? request, HttpContext http, StatefulBookingExecutor exec, CommitService commit, CancellationToken ct)
            => CommitAsync(request, http, exec, commit, isPut: false, ct)).WithTags("Commit");
        nsk.MapPut("/v3/booking", (CommitRequest? request, HttpContext http, StatefulBookingExecutor exec, CommitService commit, CancellationToken ct)
            => CommitAsync(request, http, exec, commit, isPut: true, ct)).WithTags("Commit");

        nsk.MapGet("/v2/booking/hold/available", async (HttpContext http, DraftStore drafts, BookingCommands cmd) =>
        {
            var draft = await RequireDraftAsync(http, drafts);
            var (allowed, expiration) = cmd.HoldAvailability(draft);
            return allowed ? Envelope.Ok(new { expiration }) : Envelope.OkNoData();
        });

        nsk.MapDelete("/v1/booking/journeys/{journeyKey}", async (string journeyKey, HttpContext http, StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct) =>
        {
            await exec.ExecuteAsync(http.GetSession(), async c => { await cmd.RemoveJourneyAsync(c, journeyKey, ct); return true; }, ct);
            return Envelope.OkNoData();
        });

        // ---------------------------------------------------------------- passengers
        nsk.MapGet("/v1/booking/passengers", async (HttpContext http, DraftStore drafts)
            => Envelope.Ok((await RequireDraftAsync(http, drafts)).Booking.Passengers)).WithTags("Passengers");

        nsk.MapGet("/v1/booking/passengers/{passengerKey}", async (string passengerKey, HttpContext http, DraftStore drafts) =>
        {
            var draft = await RequireDraftAsync(http, drafts);
            return draft.Booking.Passengers.TryGetValue(passengerKey, out var pax) ? Envelope.Ok(pax) : throw DomainException.InvalidKey(passengerKey);
        }).WithTags("Passengers");

        nsk.MapPut("/v3/booking/passengers/{passengerKey}", (string passengerKey, PassengerUpdateRequest? request, HttpContext http,
                StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct)
            => UpdatePassengerAsync(passengerKey, request, replace: true, http, exec, cmd, ct)).WithTags("Passengers");

        nsk.MapPatch("/v3/booking/passengers/{passengerKey}", (string passengerKey, PassengerUpdateRequest? request, HttpContext http,
                StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct)
            => UpdatePassengerAsync(passengerKey, request, replace: false, http, exec, cmd, ct)).WithTags("Passengers");

        // ---------------------------------------------------------------- contacts ("primary" == "P")
        nsk.MapGet("/v1/booking/contacts", async (HttpContext http, DraftStore drafts)
            => Envelope.Ok((await RequireDraftAsync(http, drafts)).Booking.Contacts)).WithTags("Contacts");

        nsk.MapGet("/v1/booking/contacts/{contactTypeCode}", async (string contactTypeCode, HttpContext http, DraftStore drafts) =>
        {
            var draft = await RequireDraftAsync(http, drafts);
            return draft.Booking.Contacts.TryGetValue(ContactRules.NormalizeType(contactTypeCode), out var c)
                ? Envelope.Ok(c)
                : throw new DomainException(ErrorCodes.ContactNotFound, $"No contact of type '{contactTypeCode}'.");
        }).WithTags("Contacts");

        nsk.MapPost("/v1/booking/contacts", async (Contact? contact, HttpContext http, StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct) =>
        {
            if (contact is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
            var type = await exec.ExecuteAsync(http.GetSession(), c => Task.FromResult(cmd.AddContact(c, contact)), ct);
            return Envelope.Created($"/api/nsk/v1/booking/contacts/{type}");
        }).WithTags("Contacts");

        nsk.MapPut("/v1/booking/contacts/{contactTypeCode}", async (string contactTypeCode, Contact? contact, HttpContext http,
            StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct) =>
        {
            if (contact is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
            await exec.ExecuteAsync(http.GetSession(), c => { cmd.ReplaceContact(c, contactTypeCode, contact); return Task.FromResult(true); }, ct);
            return Envelope.OkNoData();
        }).WithTags("Contacts");

        nsk.MapDelete("/v1/booking/contacts/{contactTypeCode}", async (string contactTypeCode, HttpContext http,
            StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct) =>
        {
            await exec.ExecuteAsync(http.GetSession(), c => { cmd.DeleteContact(c, contactTypeCode); return Task.FromResult(true); }, ct);
            return Envelope.OkNoData();
        }).WithTags("Contacts");

        // ---------------------------------------------------------------- retrieve
        nsk.MapGet("/v2/booking/retrieve", async ([AsParameters] RetrieveQuery query, HttpContext http, StatefulBookingExecutor exec,
            RetrieveService retrieve, CancellationToken ct)
            => Envelope.Ok(await exec.ExecuteAsync(http.GetSession(), c => retrieve.RetrieveByStrategyAsync(c, query, ct), ct)))
            .WithTags("Retrieve");

        nsk.MapGet("/v1/booking/retrieve/byRecordLocator/{recordLocator}", async (string recordLocator, HttpContext http,
                StatefulBookingExecutor exec, RetrieveService retrieve, CancellationToken ct)
            => Envelope.Ok(await exec.ExecuteAsync(http.GetSession(), c => retrieve.RetrieveByRecordLocatorAsync(c, recordLocator, ct), ct)))
            .RequirePermission(PermissionLevel.Agent).WithTags("Retrieve");

        nsk.MapGet("/v1/booking/retrieve/{bookingKey}", async (string bookingKey, HttpContext http,
                StatefulBookingExecutor exec, RetrieveService retrieve, CancellationToken ct)
            => Envelope.Ok(await exec.ExecuteAsync(http.GetSession(), c => retrieve.RetrieveByBookingKeyAsync(c, bookingKey, ct), ct)))
            .RequirePermission(PermissionLevel.Agent).WithTags("Retrieve");

        nsk.MapGet("/v1/bookings/{recordLocator}", async (string recordLocator, RetrieveService retrieve, CancellationToken ct) =>
        {
            var booking = await retrieve.GetStatelessAsync(recordLocator, ct);
            return booking is null ? Envelope.NotFound() : Envelope.Ok(booking);
        }).RequirePermission(PermissionLevel.Agent).WithTags("Bookings (stateless)");

        return app;
    }

    private static async Task<IResult> CommitAsync(CommitRequest? request, HttpContext http, StatefulBookingExecutor exec,
        CommitService commit, bool isPut, CancellationToken ct)
    {
        var wasNew = await exec.ExecuteAsync(http.GetSession(), async c =>
        {
            var isNew = !c.Require().Committed;
            await commit.CommitAsync(c, request ?? new CommitRequest(), isUpdate: !isNew, ct);
            return isNew;
        }, ct);

        // Navitaire: POST commit → 201 + Location; PUT (re-commit) → 200.
        return wasNew && !isPut ? Envelope.Created("/api/nsk/v1/booking") : Envelope.OkNoData();
    }

    private static async Task<IResult> UpdatePassengerAsync(string passengerKey, PassengerUpdateRequest? request, bool replace,
        HttpContext http, StatefulBookingExecutor exec, BookingCommands cmd, CancellationToken ct)
    {
        if (request is null) throw new DomainException(ErrorCodes.NullRequest, "The request body is required.");
        await exec.ExecuteAsync(http.GetSession(), async c =>
        {
            await cmd.UpdatePassengerAsync(c, passengerKey, request, replace, ct);
            return true;
        }, ct);
        return Envelope.OkNoData();
    }

    public static async Task<BookingDraft> RequireDraftAsync(HttpContext http, DraftStore drafts)
        => await drafts.GetAsync(http.GetSession().SessionId)
           ?? throw new DomainException(ErrorCodes.NoBookingInState, "There is no booking in state for this session.");
}

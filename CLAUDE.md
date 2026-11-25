# CLAUDE.md — AI reference for the Pss engine

Read this first in every session that builds on this codebase. The specification lives one folder up:
`../NAVITAIRE_ENGINE_BLUEPRINT.md` (architecture + rules + roadmap — section numbers below refer to it) and
`../API_ENDPOINT_CATALOG.md` (every Navitaire endpoint, ✅ = latest version to implement).
Real request/response shapes: HAR files and Postman collections in `../assets/files/` — check them before inventing a shape.

## Project documentation map

| File | Purpose | Keep in sync when… |
|---|---|---|
| `README.md` | How to build, run and try the API (Docker / Visual Studio / Postman) | run steps, ports or folders change |
| `docs/API_REFERENCE.md` | Public API contract: every implemented endpoint with examples, rules, error codes, enums, demo data | **any** endpoint, request/response field, rule or error code changes |
| `docs/api-reference.html` | Visual, self-contained version of the same reference (wayfinding-sign design, no external assets). All content lives in the JS arrays `ENDPOINTS`, `FLOW`, `ERRORS`, `ENUMS`, `FLIGHTS` at the bottom of the file | same as API_REFERENCE.md — add/edit the entry in `ENDPOINTS` (and `ERRORS`) |
| `postman/Pss.postman_collection.json` + `Pss-Local.postman_environment.json` | Postman collection (10 folders, 105 requests, auto-captures token/keys/balance/recordLocator) | endpoints are added/changed — edit `tools/postman/generate_postman.py`, run `python tools/postman/generate_postman.py postman` |
| `requests/booking-flow.http` | Same happy path for VS/Rider/VS Code | the happy path changes |
| `CLAUDE.md` (this file) | Status, architecture, conventions, next steps | every session |

A Next.js customer/agent website lives in the sibling folder `../SimulatorUI` (its own `CLAUDE.md`; it calls the
gateway through a `/pss/*` rewrite). Postman / `.http` remain the way to test the API itself. When you add an endpoint,
the definition of done includes: API_REFERENCE.md section + Postman request (with a test script) + status table below.
If you change a request/response shape the UI uses, update `../SimulatorUI/src/lib/pss.ts` / `types.ts` too.

## Current status (last updated 2026-11-25)

| Phase | State | Notes |
|---|---|---|
| 0 Foundation | ✅ done, **running in Docker** | BuildingBlocks, Contracts, YARP gateway (full §8.6 table), docker-compose, seed data, unit tests |
| 1 MVP booking | 🟡 vertical slice built and running; end-to-end Postman run pending | see "Phase 1 coverage" — remaining items listed under "Next steps" |
| 2–5 | ⬜ not started | |

### Verification log

| Date | Check | Result |
|---|---|---|
| 2026-11-25 | Desk review (no SDK in the authoring sandbox) | No compile errors found; 3 runtime issues fixed (atomic projection rebuild on commit, logout removes session from expiry index, idempotent Identity seeding) |
| 2026-11-25 | `docker compose up --build` on the developer machine (Windows, Docker Desktop) | ✅ **All 9 images compiled** (first real compile). All infrastructure + 7 services + gateway running after the runtime fixes below. Databases created and seeded |
| 2026-11-25 | Gateway `GET /health` | ✅ Healthy |
| — | `dotnet test` (unit tests) | ⬜ not yet run on the dev machine |
| — | Postman folders 04 → 09 end to end | ⬜ in progress — first run of folder 04 is the next step |

### Runtime fixes found during the first Docker run (don't reintroduce)

| Symptom | Cause | Fix |
|---|---|---|
| Terminal printed SQL forever; looked like a loop | Serilog **ignores the `Logging` section** of appsettings, so EF Core logged every command at Information; the MassTransit EF outbox polls SQL every second (Booking, Payment) | Level overrides in `BuildingBlocks.Observability/ObservabilityExtensions.cs` (EF Core, ASP.NET Core, HttpClient, MassTransit, YARP → Warning). Configure log levels via a `Serilog` section/env vars, not `Logging` |
| `seq` container exited immediately | Seq 2024.1+ refuses first start without an admin password | `SEQ_FIRSTRUN_NOAUTHENTICATION: "true"` in `docker-compose.yml` (local dev only) |
| `gateway` container exited; log: *"registered a CORS policy named 'default' that conflicts with the reserved CORS policy name"* | YARP reserves the route CORS names `default` and `disable` | Policy renamed to **`pss-cors`** in `Gateway/Program.cs` and all 62 routes in `Gateway/appsettings.json` |

Known harmless warnings in every container log: `Storing keys in a directory '/root/.aspnet/DataProtection-Keys'…` and
`No XML encryptor configured…` — ASP.NET Data Protection (cookies/antiforgery), which this API does not use. Optional
cleanup later: persist keys to Redis or disable the warning.

### Phase 1 coverage

| Service | Implemented | Not yet (Phase 1 backlog) |
|---|---|---|
| Identity | `POST /api/auth/v1/token/anonymous`, `/token/user`, `GET/PUT/DELETE /api/auth/v1/token`, legacy `/api/nsk/v1|v2/token`, JWKS + openid-configuration, Redis sessions with sliding idle timeout, `SessionExpired` sweeper | role switch, token transfer, password change/reset |
| RefData | `resources/stations`, `v2/resources/markets|countries|currencies`, `resources/passengerTypes`, generic `resources/{titles|documentTypes|contactTypes|nonBinaryGenders}`, `settings/{**name}`, `utilities/stationLocalTime` — all with ETag/304 | paymentMethods resource, Redis cache + `ReferenceDataChanged` eviction |
| Inventory | flights/legs/nests/classes, internal segment search, atomic holds (conditional row updates, LegId order), confirm/release, hold sweeper, `SessionExpired` consumer | `LegClassAvailabilityChanged` events, true nesting, public ops endpoints (Phase 3) |
| Pricing | fare selection per product class, PTC discount, taxes (fixed/%; INT/DOM; PTC exemptions), internal `journeys` + `selection` pricing | promotions, fare rules, currency conversion (single currency USD for now) |
| Shopping | `POST v4/availability/search/simple`, `v4/availability/search`, non-stop + 1-stop connections, offer store in Redis (`fareAvailabilityKey` → offer) | low-fare calendar, bundles, MAC search |
| Booking | `v4/trip/sell`, `v4|v5/trip` (one-shot), `GET v1/booking`, `v2/booking/status`, `DELETE v1/booking/reset`, `POST|PUT v3/booking` commit, passengers GET/PUT/PATCH, contacts CRUD (`primary` alias), `DELETE v1/booking/journeys/{key}` (uncommitted only), `v2/booking/hold/available`, retrieve `v2/booking/retrieve` (strategies), agent `retrieve/byRecordLocator`, `retrieve/{bookingKey}`, stateless `v1/bookings/{rl}`, internal draft API for Payment | infants, `bookings/searchBy*`, booking history endpoints, comments endpoints |
| Payment | `v2/booking/payments/available`, `POST v4|v6/booking/payments` (cash = agent only; VI/MC via FakeGateway), `GET/DELETE booking/payments[/{key}]`, internal authorise (idempotent) + void, `PaymentApproved/Declined` via outbox | 3DS/DCC/MCC (Phase 4), refunds (Phase 2) |
| Notification | itinerary email on `BookingCommitted` (Mailpit in dev) | templates, culture, SMS |

## Architecture in one screen

- One project per service: `src/Services/<Name>/Pss.<Name>.Api` with folders `Domain/` (pure, unit-testable),
  `Application/` (use cases), `Infrastructure/` (EF, Redis, HTTP clients), `Endpoints/` (Minimal API), `Consumers/`.
  [DEVIATION from §22, deliberate] the blueprint shows 4 projects per service; split a service when it grows.
- No MediatR yet: endpoints call application services directly; the stateful pipeline is `StatefulBookingExecutor`.
- **Booking in state (§5):** draft JSON in Redis `pss:session:{sid}:booking`; every stateful call runs
  lock (`pss:session:{sid}:lock`, 5 s wait → 409 `core:Session:ConcurrentRequest`) → load → mutate → save.
  Booking is the **single writer** of the draft; other services change it through `/internal/booking/draft/*`.
- **Sessions:** the JWT (RS256, `sid` claim) is only a handle; `RequirePermission(level)` checks the Redis session on every
  request and slides the idle timeout. Logout/idle expiry → `SessionExpired` → Inventory releases holds, Booking drops draft.
- **Internal calls** (`/internal/...`, never routed by the gateway) forward the caller's `Authorization` header
  (`AddInternalServiceClient<TClient,TImpl>` + `ForwardSessionHandler`), so holds/drafts stay bound to the user's session.
- **Commit (§12.3)** is synchronous orchestration in `CommitService`: validate → placeholder row (booking id + record locator)
  → authorise payments → confirm holds → persist JSON document + search projections + `BookingCommitted` (EF outbox).
  Compensation: void payments / delete placeholder on failure. [DESIGN TODO] MassTransit saga when async PSP flows arrive.
- **Persistence:** DB per service, `EnsureCreated` + seeders at startup (`InitializeDatabaseAsync`). Booking stores the
  aggregate as JSON + index tables [DEVIATION from §18 — normalise in Phase 2 if reporting needs it].
  Move to EF migrations before the schema stabilises (`dotnet ef migrations add Initial -p <service>`).

## Conventions (follow them in new code)

- **Response envelope** — always return `Envelope.Ok(data)`, `Envelope.OkNoData()`, `Envelope.Created(location)`,
  `ETagResults.OkWithETag(...)` for resources/settings. Never return raw objects from public endpoints.
- **Errors** — `throw new DomainException(ErrorCodes.X, "message", httpStatus)`; add new codes to
  `BuildingBlocks.Domain/ErrorCodes.cs` (mark ours `[DESIGN]`). Stateful unknown keys → 400 `nsk:Exceptions:InvalidKey`;
  stateless not found → 404 `{ "data": null }`. Retrieve failures are always `nsk:Booking:GeneralRestriction`.
- **Permissions** — every public group/endpoint gets `.RequirePermission(PermissionLevel.Anonymous|Customer|Agent)`;
  read the session with `http.GetSession()`.
- **JSON** — `PssJson.Options` everywhere (HTTP, Redis, internal calls): camelCase, enums written as ints and read as int or
  name, lenient dates, dictionary keys untouched. Keyed collections are dictionaries (passengers, contacts, faresAvailable).
- **Keys** — only via `Pss.BuildingBlocks.Keys` (`PassengerKey`, `SegmentKey`, `JourneyKey`, `LegKey`, `FareAvailabilityKey`,
  `FareKey`, `BookingKey`, `PaymentKey`, `SeatmapReference`); they reproduce real Navitaire keys (see `KeyCodecTests`).
- **Name-resolution trap** — inside `namespace Pss.Booking.*` / `Pss.Payment.*` the simple names `Booking` / `Payment`
  resolve to the *namespaces*. Use aliases: `using BookingModel = Pss.Contracts.Booking.Booking;`,
  `using PaymentModel = Pss.Contracts.Booking.Payment;`. Same for any future type that equals a service name.
- **Time** — inject `TimeProvider`; UTC values have `Kind=Utc` (serialised with `Z`), station-local values are `Unspecified`.
- **Money** — `decimal`, `decimal(18,4)` in SQL, round with `MidpointRounding.AwayFromZero`; breakdown is always derived
  via `BookingCalculator.Recalculate` — never set totals by hand.
- **PCI** — PAN/CVV never leave `PaymentService.AddToBookingAsync`; the draft holds only the masked number and `PMTOKEN`.
- **Logging** — Serilog only. Set levels in a `Serilog:MinimumLevel` section or env vars
  (`Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Information` to see SQL). Never log request bodies.
- **Gateway** — CORS policy is `pss-cors`; never name a YARP route policy `default` or `disable` (reserved). Routes are
  ordered by `Order` (lower = more specific first).
- **Debugging a container** — `docker compose ps` (a missing service = it exited), then `docker compose logs <service> --tail 60`.
- **Config** — local defaults in `appsettings.json` point to `localhost:<port>`; docker-compose overrides with env vars
  (`Services__Booking`, `ConnectionStrings__Sql`, …). Package versions only in `Directory.Packages.props`.

## Next steps (suggested order)

1. ~~Build on the dev machine~~ ✅ done (Docker). Now: run `dotnet test Pss.sln`; run the Postman collection (folders
   04 → 09) end to end and fix any test that fails (the scripts encode the documented behaviour in `docs/API_REFERENCE.md`).
   Record results in the verification log above.
2. Finish Phase 1 backlog: concurrency test for the last seat, HAR replay contract test (§23), infants, token role/transfer,
   password endpoints, EF migrations instead of `EnsureCreated`, booking history/comments endpoints.
3. Phase 2 (§25): Seat service, Ancillary (SSRs/bundles/baggage), fees engine + promotions + fare rules in Pricing,
   change/cancel/refund flows, credit shell (Account service), divide.

## Prompt template for the next session

> Read `Project/CLAUDE.md`, then blueprint §<x> and the catalogue rows for <service>. Implement <feature> following the
> conventions in CLAUDE.md (envelope, DomainException codes, RequirePermission, stateful executor, aliases). Add unit tests.
> Document the endpoints in docs/API_REFERENCE.md, add them to tools/postman/generate_postman.py and regenerate the
> collection, then update the status tables in CLAUDE.md.

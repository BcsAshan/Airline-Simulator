# Pss — a Navitaire-style airline booking engine (.NET 8 microservices)

Clean-room implementation of the Navitaire dotREZ Digital API behaviour described in
`../NAVITAIRE_ENGINE_BLUEPRINT.md` (architecture, rules, roadmap) and `../API_ENDPOINT_CATALOG.md` (endpoints).
Demo carrier **XX "Demo Air"**, hubbed in Colombo (CMB), with 30 days of seeded flights.

Status: **Phase 0 complete, Phase 1 vertical slice implemented and running in Docker** (all images build, all
services start; end-to-end Postman run in progress) — an anonymous customer can search, sell,
add passengers/contacts, pay by card, commit, get an itinerary email and retrieve the booking; an agent can retrieve
any booking and take cash. There is **no UI yet** — use Postman (below) or the `.http` file.
See `CLAUDE.md` for detailed status, design decisions and next steps.

## Documentation

| Document | What it is |
|---|---|
| [`docs/api-reference.html`](docs/api-reference.html) | **Visual API guide** — open in any browser (works offline): clickable booking-flow route, searchable/filterable endpoint list with request/response examples and copy buttons, error codes, enums, demo flight board, test cards |
| [`docs/API_REFERENCE.md`](docs/API_REFERENCE.md) | Every implemented endpoint: request/response examples, rules, error codes, enums, demo data, test cards |
| [`postman/`](postman/) | Postman collection (105 requests, auto-captures tokens and keys) + local environment |
| [`requests/booking-flow.http`](requests/booking-flow.http) | The same happy path for Visual Studio / Rider / VS Code REST Client |
| [`CLAUDE.md`](CLAUDE.md) | Status per service, architecture, coding conventions, next steps (AI + developer reference) |
| `../NAVITAIRE_ENGINE_BLUEPRINT.md` | The full design (section numbers are referenced throughout the code) |

## Prerequisites

- .NET 8 SDK (or newer — `global.json` rolls forward). Check with `dotnet --version`.
- Docker Desktop (WSL2 backend, at least 4 GB memory) for SQL Server 2022, Redis, RabbitMQ, Seq, Jaeger and Mailpit.
- Postman (desktop app) — or Visual Studio 2022 17.6+ / VS Code REST Client for the `.http` file.

## 1. Build and test

```powershell
cd Project
dotnet build Pss.sln
dotnet test  Pss.sln
```

## 2. Run it

### Option A — everything in Docker (quickest)

Recommended: run in the background so you get your prompt back.

```cmd
docker compose up -d --build
```

The first build takes about 4–5 minutes (all 9 images build in parallel). Then:

1. **Check everything is running** (wait ~30 s for databases to be created and seeded):
   ```cmd
   docker compose ps
   ```
   You should see 15 containers with status **Up**: `identity`, `refdata`, `inventory`, `pricing`, `shopping`,
   `booking`, `payment`, `notification`, `gateway`, `sqlserver`, `redis`, `rabbitmq`, `seq`, `jaeger`, `mailpit`.
   **A service missing from the list has stopped** — see its log (step 3).
2. **Check the gateway:** open http://localhost:5000/health → `Healthy`.
3. **Read a service's log:** `docker compose logs booking --tail 50` (add `-f` to keep following).
   A healthy service ends with `Now listening on: http://[::]:8080` and `Application started`.

Without `-d` (`docker compose up --build`) the command never returns — it keeps streaming every container's log until
you press `Ctrl+C`. That is normal, not a hang.

| Task | Command |
|---|---|
| Rebuild + restart one service after a code change | `docker compose up -d --build gateway` |
| Restart one service | `docker compose restart booking` |
| Stop everything (keep data) | `docker compose down` |
| Stop and wipe all data (databases re-created and re-seeded on next start) | `docker compose down -v` |

### Option B — infrastructure in Docker, services from Visual Studio (best for debugging)

```powershell
docker compose up -d sqlserver redis rabbitmq seq jaeger mailpit
```

Open `Pss.sln` → right-click the solution → **Configure Startup Projects** → **Multiple startup projects** → set all
`Pss.*.Api` projects and `Pss.Gateway` to **Start**, with **Pss.Identity.Api first** (other services fetch its signing keys).
Press F5.

| Service      | Local URL               | Swagger                          | Database       |
|--------------|-------------------------|----------------------------------|----------------|
| Gateway      | http://localhost:5000   | –                                | –              |
| Identity     | http://localhost:5101   | http://localhost:5101/swagger    | Pss_Identity   |
| RefData      | http://localhost:5102   | http://localhost:5102/swagger    | Pss_RefData    |
| Inventory    | http://localhost:5103   | http://localhost:5103/swagger    | Pss_Inventory  |
| Pricing      | http://localhost:5104   | http://localhost:5104/swagger    | Pss_Pricing    |
| Shopping     | http://localhost:5105   | http://localhost:5105/swagger    | (Redis only)   |
| Booking      | http://localhost:5106   | http://localhost:5106/swagger    | Pss_Booking    |
| Payment      | http://localhost:5107   | http://localhost:5107/swagger    | Pss_Payment    |
| Notification | http://localhost:5108   | –                                | –              |
| Seat         | http://localhost:5110   | http://localhost:5110/swagger    | Pss_Seat       |

In Docker (option A) only the gateway (5000) and the tool UIs are published; the per-service Swagger pages are available in option B.

## 3. Try the API with Postman

1. Postman → **Import** → select both files in `postman/`:
   `Pss.postman_collection.json` and `Pss-Local.postman_environment.json`.
2. Top-right environment selector → **Pss Local**.
3. Open folder **04 Booking flow — 1 adult, card** and click **Send** on each request in order
   (or right-click the folder → **Run folder** to run it with the Collection Runner).
4. Request 12 shows the confirmed booking and its **record locator**; the itinerary email is in Mailpit.

The collection's test scripts copy values forward automatically (token → journeyKey/fareAvailabilityKey → passengerKey →
balanceDue → recordLocator/bookingKey), and the **Test Results** tab shows what passed. Travel dates default to
today + 14 / + 21 days; change `travelDate` / `returnDate` in the collection's **Variables** tab if you want other dates
(must be within the 30 seeded days).

| Folder | Scenario |
|---|---|
| 00 Health | Gateway is up |
| 01 Auth & session | Anonymous / agent / customer tokens, token info, refresh, legacy v2 token, logout |
| 02 Reference data | Stations, markets, countries, currencies, passenger types, code lists, settings, ETag → 304 |
| 03 Availability | One-way, date range, CMB→LHR connection via DXB, non-stop filter, round trip |
| 04 Booking flow | Token → search → sell → passenger → contact → pay by card → commit → status → logout |
| 05 Round trip family | 2 adults + 1 child CMB⇄SIN, child discount, MasterCard |
| 06 One-shot booking | `POST v5/trip` does sell + passenger + contact in one call |
| 07 Manage booking | Retrieve by record locator + last name / email, change contact, re-commit with `PUT` |
| 08 Agent | Agent login, stateless read, retrieve by record locator / booking key, cash booking |
| 09 Error cases | 401, missing booking, agent-only, invalid key, name rules, age rules, card errors, declined card |

Run 04, 05 or 06 before 07 and 08 — those folders use the record locator of the last booking you made.

Tips:
- A token is a session: it ends after **15 idle minutes** or on logout, and held seats are released. If you get
  `401 core:Unauthorized`, run an "Anonymous token" request again.
- Only one booking per session. Starting a new sell flow in the same session adds to the current booking — log out
  between scenarios, as the folders do.
- Every response has an `X-Correlation-Id` header; search for it in Seq to see the logs of that call in every service.

## Where to look while it runs

| What | URL |
|---|---|
| Itinerary emails | http://localhost:8025 (Mailpit) |
| Logs from all services | http://localhost:5341 (Seq) |
| Distributed traces | http://localhost:16686 (Jaeger) |
| Message broker | http://localhost:15672 (guest / guest) |

## Demo data

- Routes (daily): CMB⇄DXB, CMB⇄SIN, CMB⇄BKK, CMB⇄MLE, CMB⇄DEL, CMB⇄KUL, CMB⇄JAF, DXB⇄LHR (so CMB→LHR connects via DXB).
- Classes: J/C/D (business, product BZ) and Y/B (EF), M/H (ES), Q/L (EC). Fares in USD; CHD 25 % off.
  Example: 1 adult CMB→DXB in L = 180 + 60 levy + 25 fuel = **265 USD**.
- Users: anonymous via `token/anonymous`; agent `DEF / agent01 / Password1!`; customer `WWW / customer@demo.test / Password1!`.
- Test cards: `4111111111111111` and `5555555555554444` approved · `4000000000000002` declined · `4000000000009995` insufficient funds.

Full tables (flights, fares, taxes) are in `docs/API_REFERENCE.md` §12.

## Troubleshooting

| Problem | Fix |
|---|---|
| A container is missing from `docker compose ps` | It started and then exited — `docker compose logs <service> --tail 60` shows why |
| `WRN … DataProtection-Keys …` / `No XML encryptor configured` in every log | Harmless. ASP.NET cookie/antiforgery key storage, which this API doesn't use |
| Gateway exits: *"CORS policy named 'default' conflicts with the reserved CORS policy name"* | Fixed — the policy is now `pss-cors`. Rebuild: `docker compose up -d --build gateway` |
| Seq exits on first start | Fixed — `SEQ_FIRSTRUN_NOAUTHENTICATION` is set. Run `docker compose up -d seq` |
| Terminal prints SQL commands non-stop | Fixed — database/framework logs are now Warning-only. To see SQL again set env var `Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Information` |
| `docker compose up` never returns / keeps printing | Normal: it streams logs until Ctrl+C. Use `docker compose up -d --build` to run in the background and `docker compose logs -f booking` to watch one service |
| `Database not ready (n/30)` keeps repeating | SQL Server isn't starting — check `docker compose logs sqlserver` (usually Docker Desktop has under 2 GB memory) |
| Port 1433 already in use | Stop the local SQL Server service, or change the port in `docker-compose.yml` and the `appsettings.json` files |
| 401 on every call right after starting (option B) | Identity wasn't up when the others started — restart the other services |
| Search returns journeys with empty `fares` | Sold out for that passenger count, or the date is outside the seeded 30 days |
| `nsk:Exceptions:InvalidKey` on sell | Offers expire 30 minutes after the search — search again |
| `502 Bad Gateway` | The service for that route isn't running (or belongs to a later phase) |

## Solution layout

```
src/BuildingBlocks   Domain (errors), Keys (Navitaire key codec), Api (envelope, JSON, ETag, Server-Timing),
                     Session (JWT, Redis sessions, session lock, permission filter), Messaging (MassTransit + outbox),
                     Persistence (EF helpers), Observability (Serilog + OpenTelemetry)
src/Contracts        Booking (public JSON model), Events (RabbitMQ), Internal (service-to-service DTOs)
src/Gateway          YARP gateway with the blueprint §8.6 route table
src/Services         Identity, RefData, Inventory, Pricing, Shopping, Booking, Payment, Notification, Seat
tools/Pss.SeedData   Demo carrier XX data shared by the seeders
tests/Pss.UnitTests  Key codec (HAR keys), JSON/envelope, pricing, inventory math, booking rules
docs/                API_REFERENCE.md
postman/             Postman collection + environment
requests/            booking-flow.http
```

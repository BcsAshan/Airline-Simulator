# Pss Engine — API Reference (Phase 1 + Phase 2 seats)

This document describes every public endpoint that is implemented today, how to call it, and what it returns.
The API copies the Navitaire dotREZ Digital API contract, so routes, field names and error codes match Navitaire
wherever possible. Items marked **[DESIGN]** are our own additions where Navitaire documents no equivalent.

For a ready-made test client, import `postman/Pss.postman_collection.json` and `postman/Pss-Local.postman_environment.json`
into Postman (see the README). The collection copies tokens, keys and amounts from one response into the next request,
so you can run it top to bottom without copy-pasting.

---

## Contents

1. [Basics](#1-basics)
2. [The booking flow in one picture](#2-the-booking-flow-in-one-picture)
3. [Authentication & session — Identity](#3-authentication--session--identity)
4. [Reference data — RefData](#4-reference-data--refdata)
5. [Availability — Shopping](#5-availability--shopping)
6. [Booking in state — Booking](#6-booking-in-state--booking)
7. [Payments — Payment](#7-payments--payment)
8. [Commit, status and retrieve — Booking](#8-commit-status-and-retrieve--booking)
9. [Agent-only endpoints](#9-agent-only-endpoints)
10. [Error codes](#10-error-codes)
11. [Enumerations](#11-enumerations)
12. [Demo data & test cards](#12-demo-data--test-cards)
13. [Internal & operational endpoints](#13-internal--operational-endpoints)
14. [Seats — Seat service (Phase 2)](#14-seats--seat-service-phase-2)

---

## 1. Basics

### Base URL

Always call the **gateway**: `http://localhost:5000`. It routes each path to the right service.
(Each service also has its own Swagger page at `http://localhost:51xx/swagger` — see the README port table.)

### Authentication

1. Get a token: `POST /api/auth/v1/token/anonymous` (customers on the web) or `POST /api/auth/v1/token/user` (agents / logged-in customers).
2. Send it on every other request: `Authorization: Bearer <token>`. A raw token without the `Bearer ` prefix is also accepted, as in dotREZ.

The token is only a **handle to a server-side session**. The session expires after **15 minutes without a request**
(each request extends it). When a session expires or you log out, held seats are released and the booking in state is lost.
An expired session returns **401** even if the token itself is still valid.

### Permission levels

| Level | Who | Typical use |
|---|---|---|
| Anonymous | any valid session (including anonymous token) | search, sell, pay, commit, retrieve own booking |
| Customer | logged-in customer or agent | (Phase 4: profile, my bookings) |
| Agent | agent login only | retrieve by record locator / key, stateless booking read, cash payments |

Calling an agent endpoint with a non-agent session returns **401** `nsk:Authorization:AgentOnly`.

### "Booking in state" (very important)

Navitaire works with one **draft booking per session**:

- `POST /trip/sell` creates the draft and **holds seats** until the session ends.
- Every `/booking/...` (singular) endpoint changes that draft. There is **no record locator in the URL**.
- Nothing is saved until you **commit** (`POST /api/nsk/v3/booking`). After commit the draft stays in the session, so you can change it and commit again with `PUT`.
- `/bookings/...` (plural) endpoints are **stateless** — they work on a stored booking by record locator.
- Only one stateful call per session at a time. A second concurrent call waits up to 5 s, then returns **409** `core:Session:ConcurrentRequest`.

### Response envelope

Every response uses the same wrapper:

```json
// success with data
{ "data": { ... } }

// success with warnings / information
{ "data": { ... }, "messages": [ { "code": "...", "value": "...", "type": "Warning", "status": 0, "details": null } ] }

// command success (no data)
{ "data": null }

// failure (400 / 401 / 404 / 409 / 500)
{
  "data": null,
  "errors": [
    {
      "id": "6f1c…",
      "code": "nsk:Passenger:FirstNameRequired",
      "message": "First name is required for passenger MCFBRFQ-.",
      "rawMessage": "…only in Development…",
      "type": "Validation",
      "details": null
    }
  ]
}
```

### HTTP status codes

| Status | Meaning |
|---|---|
| 200 | Read, update, sell (returns the booking), re-commit (`PUT`) |
| 201 | Created — token, contact, payment, first commit. A `Location` header points at the new resource |
| 304 | Not modified — `If-None-Match` matched the `ETag` (reference data only) |
| 400 | Validation or business-rule error (most business errors are 400, as in Navitaire) |
| 401 | No / invalid / expired token, or the endpoint needs a higher permission level |
| 404 | Stateless resource not found (`{ "data": null }`) |
| 409 | Concurrent stateful call, or the stored booking was changed by another session **[DESIGN]** |
| 500 / 502 / 503 | Unexpected error / downstream service failed / downstream service not reachable |

### Formats

- JSON field names are **camelCase**.
- **Enums are returned as numbers** and accepted as a number *or* a name: `"gender": "Male"` and `"gender": 1` are the same.
- **Dates**: requests accept `2026-12-10`, `2026-12-1` or full ISO 8601. UTC values end in `Z`
  (`departureTimeUtc`). Station-local times have no offset (`designator.departure`).
- **Money**: decimal numbers with a `currencyCode` (Phase 1 sells in **USD** only).
- **Keyed collections are JSON objects**, not arrays: `passengers`, `contacts`, `faresAvailable`, `journeysAvailableByMarket`, `breakdown.passengers`.
- **Keys** (`journeyKey`, `passengerKey`, `paymentKey` …) are opaque Base64 strings. Copy them from responses; don't build them yourself.
  The first adult in a booking is always `MCFBRFQ-` (which decodes to `0!ADT`), the second `MSFBRFQ-`.

### Useful headers

| Header | Direction | Use |
|---|---|---|
| `Authorization` | request | `Bearer <token>` |
| `Content-Type: application/json` | request | all bodies |
| `If-None-Match` / `ETag` | both | reference data caching → `304` |
| `Location` | response | on `201` |
| `X-Correlation-Id` | both | optional on request; always returned. Search it in Seq to see the logs of one call across all services |
| `Server-Timing` | response | `api;dur=…, session;dur=…, gateway;dur=…` |

---

## 2. The booking flow in one picture

```
 1  POST /api/auth/v1/token/anonymous                     → token
 2  POST /api/nsk/v4/availability/search/simple           → journeyKey + fareAvailabilityKey
 3  POST /api/nsk/v4/trip/sell                            → booking in state, seats held, passengerKeys
 4  PUT  /api/nsk/v3/booking/passengers/{passengerKey}    → names, DOB, gender   (repeat per passenger)
 5  POST /api/nsk/v1/booking/contacts                     → primary contact "P"
 5b GET  /api/nsk/v3/booking/seatmaps/journey/{key}       → seat maps (optional seat selection, see §14)
    POST /api/nsk/v2/booking/passengers/{pk}/seats/{unitKey} → seat + seat fee on the booking
 6  GET  /api/nsk/v2/booking/payments/available           → payment methods + required fields
 7  POST /api/nsk/v6/booking/payments                     → card / cash for the balance due
 8  POST /api/nsk/v3/booking                              → COMMIT → record locator
 9  GET  /api/nsk/v2/booking/status                       → confirmed booking
10  DELETE /api/auth/v1/token                             → log out (always do this)
```

Shortcut: `POST /api/nsk/v5/trip` does steps 3–5 in one call.

---

## 3. Authentication & session — Identity

### POST /api/auth/v1/token/anonymous

Creates a session for the anonymous web user of the given application. No `Authorization` header.

Request (body optional):

```json
{ "applicationName": "dotREZWeb" }
```

Response **201**:

```json
{
  "data": {
    "token": "eyJhbGciOiJSUzI1NiIs…",
    "expires": "2026-11-26T09:15:02.1234567Z",
    "idleTimeoutInMinutes": 15,
    "cultureCode": "en-US",
    "currencyCode": "USD",
    "roleCode": "WWWA",
    "locationCode": "WWW",
    "domainCode": "WW2",
    "organizationCode": "SYSTEM",
    "userKey": "1",
    "personKey": null,
    "multiFactorRequired": false
  }
}
```

`expires` is the JWT lifetime (24 h). The session itself still ends after 15 idle minutes.

### POST /api/auth/v1/token/user

Logs on an agent or customer.

```json
{
  "applicationName": "dotREZWeb",
  "credentials": { "domain": "DEF", "username": "agent01", "password": "Password1!", "location": "CMB" }
}
```

`domain` defaults to `DEF`; `location` is optional. Response **201**, same shape as above.

| Error | When |
|---|---|
| 401 `nsk:Authentication:InvalidCredentials` [DESIGN] | wrong user / password, inactive user, or an anonymous (virtual) user |
| 400 `nsk-server:PasswordExpired` | password must be changed first |
| 400 `core:Validation:NullRequest` | credentials missing |

### GET /api/auth/v1/token — session information

Response **200**:

```json
{
  "data": {
    "sessionId": "1f0c…", "userType": 3, "agentCode": "ApiAnonymous", "roleCode": "WWWA",
    "domainCode": "WW2", "organizationCode": "SYSTEM", "locationCode": "WWW",
    "applicationName": "dotREZWeb", "cultureCode": "en-US", "currencyCode": "USD",
    "idleTimeoutInMinutes": 15, "userKey": "1", "personKey": null
  }
}
```

`userType`: 1 Customer, 2 Agent, 3 Anonymous.

### PUT /api/auth/v1/token — keep alive / refresh

Extends the session and returns a **new token** for the same session (same shape as token creation, status 200).

### DELETE /api/auth/v1/token — log out

Response **200** `{ "data": null }`. Ends the session immediately, releases held seats and drops the booking in state.
Always call it when a flow is finished.

### Legacy token routes (for the Navitaire Postman collections)

| Route | Behaviour |
|---|---|
| `POST /api/nsk/v1/token`, `POST /api/nsk/v2/token` | Empty body `{}` → anonymous; body with `credentials` → user. Returns `{ "data": { "token", "idleTimeoutInMinutes" } }` (201) |
| `GET`, `PUT`, `DELETE /api/nsk/v1/token` and `/v2/token` | Same as the `/api/auth/v1/token` versions |

---

## 4. Reference data — RefData

All need a token (any level). All return an **ETag**; send it back as `If-None-Match` to get **304** with no body.

| Method & path | Returns |
|---|---|
| `GET /api/nsk/v1/resources/stations` | `[ { stationCode, name, shortName, countryCode, macCode, currencyCode, timeZoneCode, inActive } ]` |
| `GET /api/nsk/v1/resources/stations/{stationCode}` | one station, or 404 |
| `GET /api/nsk/v2/resources/markets` | `[ { locationCode, travelLocationCode, inActive, locationType, travelLocationType, … } ]` — the city pairs that are sold |
| `GET /api/nsk/v2/resources/countries` | `[ { countryCode, countryCode3C, name, defaultCurrencyCode, hasProvinceStates, inActive, … } ]` |
| `GET /api/nsk/v2/resources/countries/{countryCode}` | one country, or 404 |
| `GET /api/nsk/v2/resources/currencies` | `[ { currencyCode, description, symbol, decimalPlaces, inActive } ]` |
| `GET /api/nsk/v1/resources/passengerTypes` | `[ { passengerTypeCode, name, minimumAge, maximumAge, nonAdult, allowed, inActive, typeIndicator } ]` |
| `GET /api/nsk/v1/resources/{listName}` | code lists: `titles`, `documentTypes`, `contactTypes`, `nonBinaryGenders` → `[ { code, name, inActive } ]`. Unknown list → 400 `core:Validation:ResourceNotFound` |
| `GET /api/nsk/v1/settings/{name}` | settings JSON. Names: `booking/passenger`, `booking/contact`, `booking/general`, `payment`, `general` |
| `GET /api/nsk/v1/utilities/stationLocalTime/{stationCode}` | `{ stationCode, localTime, utcOffsetMinutes }` (no ETag) |

Example — `GET /api/nsk/v1/resources/passengerTypes`:

```json
{
  "data": [
    { "nonAdult": false, "passengerTypeCode": "ADT", "name": "Adult", "allowed": true, "inActive": false, "typeIndicator": 1, "minimumAge": 12, "maximumAge": 120 },
    { "nonAdult": true,  "passengerTypeCode": "CHD", "name": "Child", "allowed": true, "inActive": false, "typeIndicator": 2, "minimumAge": 2,  "maximumAge": 11 },
    { "nonAdult": true,  "passengerTypeCode": "INF", "name": "Infant", "allowed": true, "inActive": false, "typeIndicator": 2, "minimumAge": 0, "maximumAge": 1 }
  ]
}
```

---

## 5. Availability — Shopping

### POST /api/nsk/v4/availability/search/simple

One-way search for one market over a date range (up to 14 days).

```json
{
  "origin": "CMB",
  "destination": "DXB",
  "beginDate": "2026-12-10",
  "endDate": "2026-12-11",
  "passengers": { "types": [ { "type": "ADT", "count": 2 }, { "type": "CHD", "count": 1 } ] },
  "codes": { "currencyCode": "USD", "promotionCode": null },
  "filters": { "maxConnections": 1, "flightType": "All", "carrierCode": "XX" },
  "numberOfFaresPerJourney": 4,
  "taxesAndFees": "TaxesAndFees"
}
```

| Field | Default | Notes |
|---|---|---|
| `origin`, `destination` | required | 3-letter station codes |
| `beginDate` | required | |
| `endDate` | = `beginDate` | max 14 days range |
| `passengers.types` | 1 × ADT | max 9 passengers |
| `codes.currencyCode` (or top-level `currencyCode`) | session currency (USD) | only USD in Phase 1 |
| `filters.maxConnections` | 1 | 0 = non-stop only |
| `filters.flightType` | All | `NonStop`, `Connect`, `All` |
| `numberOfFaresPerJourney` | 1 | one fare per product class (EC, ES, EF, BZ), cheapest first |
| `taxesAndFees` | None | `Taxes`/`TaxesAndFees` adds taxes to `revenueTotal`/`discountedTotal` |

Response **200** (shortened):

```json
{
  "data": {
    "results": [
      {
        "trips": [
          {
            "multipleOriginStations": false,
            "multipleDestinationStations": false,
            "date": "2026-12-10T00:00:00",
            "journeysAvailableByMarket": {
              "CMB|DXB": [
                {
                  "flightType": 1,
                  "stops": 0,
                  "designator": { "origin": "CMB", "destination": "DXB", "departure": "2026-12-10T08:30:00", "arrival": "2026-12-10T11:30:00" },
                  "journeyKey": "WFh_IDEwMX4gfn5DTUJ_MTIvMTAvMjAyNiAwODozMH5EWEJ_MTIvMTAvMjAyNiAxMTozMH5_",
                  "segments": [
                    {
                      "segmentKey": "WFh_IDEwMX4gfn5DTUJ_…",
                      "identifier": { "identifier": "101", "carrierCode": "XX", "opSuffix": null },
                      "designator": { "origin": "CMB", "destination": "DXB", "departure": "2026-12-10T08:30:00", "arrival": "2026-12-10T11:30:00" },
                      "international": true,
                      "legs": [
                        {
                          "legKey": "NjM5…",
                          "legInfo": { "departureTimeUtc": "2026-12-10T03:00:00Z", "arrivalTimeUtc": "2026-12-10T07:30:00Z",
                                       "equipmentType": "321", "capacity": 188, "lid": 188, "sold": 0, "status": 0, "…": "…" },
                          "flightReference": "20261210 XX 101 CMBDXB"
                        }
                      ]
                    }
                  ],
                  "fares": [
                    {
                      "fareAvailabilityKey": "ZDNlMWM…IUw-",
                      "details": [ { "availableCount": 27, "status": 1, "reference": "MTd_TH5YWH5…" } ],
                      "isSumOfSector": false
                    }
                  ]
                }
              ]
            }
          }
        ]
      }
    ],
    "faresAvailable": {
      "ZDNlMWM…IUw-": {
        "fareAvailabilityKey": "ZDNlMWM…IUw-",
        "isSumOfSector": false,
        "totals": { "fareTotal": 360, "revenueTotal": 360, "publishedTotal": 360, "discountedTotal": 360, "loyaltyTotal": 0 },
        "fares": [
          {
            "classOfService": "L", "fareBasisCode": "LECOW", "productClass": "EC", "ruleNumber": "EC01", "travelClassCode": "Y",
            "reference": "MTd_TH5YWH5…",
            "passengerFares": [
              {
                "passengerType": "ADT", "fareAmount": 180, "discountedFare": 180, "multiplier": 2,
                "serviceCharges": [
                  { "type": 0, "code": null,  "amount": 180, "currencyCode": "USD" },
                  { "type": 5, "code": "LKD", "amount": 60,  "currencyCode": "USD", "detail": "Sri Lanka Embarkation Levy" },
                  { "type": 4, "code": "YQ",  "amount": 25,  "currencyCode": "USD", "detail": "Fuel Surcharge" }
                ]
              }
            ]
          }
        ]
      }
    },
    "currencyCode": "USD",
    "includeTaxesAndFees": false,
    "bundleOffers": null
  }
}
```

How to read it:

- Pick a journey from `journeysAvailableByMarket["CMB|DXB"]`, then one of its `fares[].fareAvailabilityKey`.
- Look the price up in `faresAvailable[fareAvailabilityKey]`. `passengerFares` lists one price **per passenger type**; `multiplier` is how many passengers of that type you searched for.
- `serviceCharges[].type`: 0 fare, 1 discount (negative), 4 travel fee, 5 tax (see [Enumerations](#11-enumerations)).
- Connections (e.g. CMB→LHR via DXB) have 2 segments, `flightType` 4 and `isSumOfSector: true`.
- A journey with an empty `fares` array is sold out for your passenger count.
- **Offers expire after 30 minutes.** After that, selling the key returns `nsk:Exceptions:InvalidKey`; search again.

### POST /api/nsk/v4/availability/search — multi-criteria (round trip, multi-city)

One result per criterion, in the same order.

```json
{
  "criteria": [
    { "stations": { "originStationCodes": ["CMB"], "destinationStationCodes": ["SIN"] }, "dates": { "beginDate": "2026-12-10" } },
    { "stations": { "originStationCodes": ["SIN"], "destinationStationCodes": ["CMB"] }, "dates": { "beginDate": "2026-12-17" } }
  ],
  "passengers": { "types": [ { "type": "ADT", "count": 1 } ] },
  "codes": { "currencyCode": "USD" },
  "numberOfFaresPerJourney": 2
}
```

Outbound journeys are in `results[0]`, return journeys in `results[1]`. Sell both keys in one `trip/sell` call.

---

## 6. Booking in state — Booking

### POST /api/nsk/v4/trip/sell

Creates the booking in state (or adds journeys to it) and **holds the seats**.

```json
{
  "keys": [
    { "journeyKey": "<from availability>", "fareAvailabilityKey": "<from availability>" }
  ],
  "passengers": { "types": [ { "type": "ADT", "count": 2 }, { "type": "CHD", "count": 1 } ] },
  "currencyCode": "USD",
  "promotionCode": null,
  "preventOverlap": true
}
```

Rules:

- The **passenger count is fixed** by the first sell. Later sells may leave `passengers` out, or must send the same mix.
- At least one adult. `INF` is not a passenger type here (infants come in Phase 2 via `infantCount`).
- The price is recalculated for the passengers you sell (it can differ from the search if you searched for a different mix).
- `keys` may be empty: that creates a booking with passengers only.

Response **200**: the full booking (see [Booking object](#the-booking-object)). Note the new `passengers` keys — you need them next.

| Error | When |
|---|---|
| `nsk:Exceptions:InvalidKey` | bad or expired `journeyKey` / `fareAvailabilityKey` |
| `nsk:Availability:ClassNotAvailable` [DESIGN] | seats were taken since the search — search again |
| `nsk:Booking:PassengerTypesMismatch` [DESIGN] | passenger mix differs from the booking |
| `nsk:Booking:JourneyAlreadySold` / `JourneyOverlap` [DESIGN] | same journey twice / journeys overlap in time |
| `nsk:Availability:MaxBookingPassengers` | more than 9 passengers |
| `nsk:Passenger:TypeNotFound`, `nsk:Passenger:InvalidPassengerType` | unknown type / no adult / INF used as a type |
| `nsk:Validation:UnknownSellCurrencyCode` | currency other than USD |
| `nsk:Booking:NotSupportedYet` [DESIGN] | `infantCount` > 0 (Phase 2) |

### POST /api/nsk/v5/trip (also /v4/trip) — one-shot booking

Sell + passenger details + contact in a single call. **Starts a new booking in state** (any uncommitted draft is discarded and its seats released).

```json
{
  "journeys": { "keys": [ { "journeyKey": "<…>", "fareAvailabilityKey": "<…>" } ] },
  "passengers": [
    { "passenger": { "passengerTypeCode": "ADT",
                     "name": { "first": "Ashan", "last": "Dias", "title": "MR" },
                     "info": { "nationality": "LK", "residentCountry": "LK", "gender": "Male", "dateOfBirth": "1990-05-14" } } }
  ],
  "contact": {
    "name": { "first": "Ashan", "last": "Dias" },
    "emailAddress": "ashan@example.com",
    "phoneNumbers": [ { "type": "Mobile", "number": "+94770000000" } ]
  },
  "currencyCode": "USD"
}
```

`contact.contactTypeCode` defaults to `P`. You can also send `contacts: [ … ]`. Response **200**: the booking.

### GET /api/nsk/v1/booking

Returns the booking in state. **400** `nsk:Booking:NoBookingInState` if there is none.

### DELETE /api/nsk/v1/booking/reset

Drops the booking in state and releases any seats not yet committed. Response `{ "data": null }`.

### DELETE /api/nsk/v1/booking/journeys/{journeyKey}

Removes a journey that has **not been committed yet** and releases its seats. Committed journeys return
`nsk:Booking:JourneyNotChangeable` (cancellation arrives in Phase 2).

### GET /api/nsk/v2/booking/hold/available

Can this booking be committed without full payment?

- `{ "data": { "expiration": "2026-11-26T10:00:00Z" } }` → yes, until that time.
- `{ "data": null }` → no.

Rule: the first flight must be **at least 72 hours away**. The hold lasts up to 24 hours.

### Passengers

| Method & path | Body | Response |
|---|---|---|
| `GET /api/nsk/v1/booking/passengers` | – | `{ "<passengerKey>": Passenger, … }` |
| `GET /api/nsk/v1/booking/passengers/{passengerKey}` | – | one Passenger |
| `PUT /api/nsk/v3/booking/passengers/{passengerKey}` | full passenger (replaces name/info) | `{ "data": null }` |
| `PATCH /api/nsk/v3/booking/passengers/{passengerKey}` | only the fields to change | `{ "data": null }` |

```json
{
  "name": { "first": "Ashan", "middle": null, "last": "Dias", "title": "MR", "suffix": null },
  "info": { "nationality": "LK", "residentCountry": "LK", "gender": "Male", "dateOfBirth": "1990-05-14", "declaredGender": null },
  "customerNumber": null,
  "program": null
}
```

Rules:

- Names may contain letters (including accents), spaces, `-` and `'`; not ``~ ` ! @ # $ % ^ & * ( ) _ + = [ ] \ | ; : " , < > / ? { }``. Max 32 characters per part; title max 6.
  → `nsk:Validation:InvalidFirstNameCharacters` / `…LastName…` / `…MiddleName…`
- `dateOfBirth` must fit the passenger type's age range **on the first flight date** (ADT 12+, CHD 2–11) → `nsk:Passenger:InvalidDateOfBirth`.
- `declaredGender` (e.g. `M`, `F`, `X`) wins over `gender`: `F` → Female, anything else → Male.
- Changing `passengerTypeCode` is not supported yet (Phase 2).
- First and last name are **required at commit**, not here.

### Contacts

`contactTypeCode` is one character; `P` = primary. The word `primary` in the URL means `P`.

| Method & path | Response |
|---|---|
| `GET /api/nsk/v1/booking/contacts` | `{ "P": Contact, … }` |
| `GET /api/nsk/v1/booking/contacts/{contactTypeCode}` | one Contact, or 400 `nsk:Booking:ContactNotFound` |
| `POST /api/nsk/v1/booking/contacts` | **201**, `Location: /api/nsk/v1/booking/contacts/P` — 400 `nsk:Booking:ContactTypeExists` if that type exists (use PUT) |
| `PUT /api/nsk/v1/booking/contacts/{contactTypeCode}` | `{ "data": null }` — add or replace |
| `DELETE /api/nsk/v1/booking/contacts/{contactTypeCode}` | `{ "data": null }` |

```json
{
  "contactTypeCode": "P",
  "name": { "first": "Ashan", "last": "Dias", "title": "MR" },
  "emailAddress": "ashan@example.com",
  "phoneNumbers": [ { "type": "Mobile", "number": "+94770000000" } ],
  "address": { "lineOne": "1 Galle Road", "city": "Colombo", "countryCode": "LK", "postalCode": "00300" },
  "cultureCode": "en-US",
  "distributionOption": "Email"
}
```

`distributionOption: "Email"` (2) sends the itinerary email when the booking is first committed.

### The booking object

Returned by sell, trip, `GET booking`, `GET booking/status` and retrieve. Shortened:

```json
{
  "bookingKey": null,                 // set after commit, e.g. "MTIhSzdQTTJRIWZhbHNl"
  "recordLocator": null,              // 6 characters after commit, e.g. "K7PM2Q"
  "currencyCode": "USD",
  "info": { "status": 0, "paidStatus": 0, "priceStatus": 2, "channelType": 5,
            "bookedDate": "…Z", "createdDate": null, "modifiedDate": null, "expirationDate": null, "owningCarrierCode": null },
  "sales": { "created": { "agentCode": "ApiAnonymous", "domainCode": "WW2", "locationCode": "WWW", "organizationCode": "SYSTEM" } },
  "typeOfSale": { "residentCountry": null, "promotionCode": null, "fareTypes": [] },
  "hold": null,                       // { "expiration": "…" } when status = 1 (Hold)
  "breakdown": {
    "totalAmount": 265, "balanceDue": 265, "authorizedBalanceDue": 265, "totalToCollect": 265, "totalCharged": 265,
    "journeyTotals": { "totalAmount": 265, "totalTax": 85, "totalDiscount": 0, "totalPoints": 0 },
    "journeys": { "<journeyKey>": { "journeyKey": "…", "totalAmount": 265, "totalTax": 85, "totalDiscount": 0 } },
    "passengers": { "MCFBRFQ-": { "passengerKey": "MCFBRFQ-", "services": null } },
    "passengerTotals": { "services": null }
  },
  "contacts": { "P": { "contactTypeCode": "P", "name": { … }, "emailAddress": "…", "distributionOption": 2 } },
  "passengers": {
    "MCFBRFQ-": { "passengerKey": "MCFBRFQ-", "passengerTypeCode": "ADT", "name": { "first": "Ashan", "last": "Dias" },
                  "info": { "gender": 1, "dateOfBirth": "1990-05-14T00:00:00", "nationality": "LK" }, "fees": [], "weightCategory": 1 }
  },
  "journeys": [
    { "journeyKey": "…", "flightType": 1, "stops": 0, "designator": { … },
      "segments": [ { "segmentKey": "…", "identifier": { "identifier": "101", "carrierCode": "XX" }, "status": 3,
                      "cabinOfService": "Y", "fares": [ { "classOfService": "L", "fareBasisCode": "LECOW",
                      "passengerFares": [ { "passengerType": "ADT", "serviceCharges": [ … ] } ] } ],
                      "passengerSegment": { "MCFBRFQ-": { "liftStatus": 0, "seats": [], "ssrs": [] } }, "legs": [ … ] } ] }
  ],
  "payments": [],
  "comments": [],
  "history": []
}
```

Key fields:

- `info.status`: 0 in state (not committed), 1 Hold, 2 Confirmed.
- `info.paidStatus`: 0 UnderPaid, 1 PaidInFull, 2 OverPaid.
- `breakdown.balanceDue` = total − payments that were not declined. **Pay exactly this amount.**
- `breakdown` is always recalculated by the server; you never set it.
- `segments[].status`: 3 held (not committed), 6 confirmed.

---

## 7. Payments — Payment

### GET /api/nsk/v2/booking/payments/available

Payment methods for the current session, with the fields each needs. Needs a booking in state.

```json
{
  "data": {
    "VI": {
      "paymentMethodCode": "VI", "name": "Visa", "paymentMethodType": 0, "allowed": true, "maxInstallments": 1,
      "paymentMethodFields": {
        "ACCTNO":                { "paymentMethodFieldCode": "ACCTNO", "name": "AccountNumber", "requiredType": 1, "valueMinLength": 13, "valueMaxLength": 19 },
        "EXPDAT":                { "paymentMethodFieldCode": "EXPDAT", "name": "Expiry Date", "requiredType": 1 },
        "CC::AccountHolderName": { "paymentMethodFieldCode": "CC::AccountHolderName", "requiredType": 1, "valueMaxLength": 30 },
        "CC::VerificationCode":  { "paymentMethodFieldCode": "CC::VerificationCode", "requiredType": 1, "valueMaxLength": 4 }
      }
    },
    "MC": { "…": "…" }
  }
}
```

`CA` (cash) is only listed for **agent** sessions.

### POST /api/nsk/v6/booking/payments (also /v4)

Adds a payment to the booking in state. Cards are **not charged yet** — they are authorised at commit.

Card:

```json
{
  "paymentMethodCode": "VI",
  "amount": 265,
  "currencyCode": "USD",
  "installments": 1,
  "paymentFields": {
    "ACCTNO": "4111111111111111",
    "EXPDAT": "12/2030",
    "CC::AccountHolderName": "Ashan Dias",
    "CC::VerificationCode": "123"
  }
}
```

Cash (agent only):

```json
{ "paymentMethodCode": "CA", "amount": 265, "currencyCode": "USD" }
```

Response **201** `{ "data": null }` with `Location: /api/nsk/v1/booking/payments/{paymentKey}`.

- `EXPDAT` accepts `MM/yyyy`, `MM/yy`, `2030-12-01` or `2030-12-01T00:00:00`.
- The card number is turned into a token straight away. The booking only keeps the masked number (`XXXXXXXXXXXX1111`) and the token (`details.fields.PMTOKEN`). The CVV is never stored.
- Before commit, a card payment has status 1 (Received) and cash has 3 (Approved).

| Error | When |
|---|---|
| `nsk:Payment:InvalidPaymentMethod` [DESIGN] | unknown method, or cash without an agent session |
| `nsk:Payment:PaymentFieldRequired` [DESIGN] | a required field is missing (`details.field` names it) |
| `nsk:Payment:InvalidAccountNumber` [DESIGN] | card number fails the Luhn check |
| `nsk:Payment:CardExpired` [DESIGN] | expiry in the past or unreadable |
| `nsk:Payment:InvalidAmount` [DESIGN] | amount ≤ 0 or more than `balanceDue` |
| `nsk:Currency:InvalidCurrencyCode` | currency differs from the booking |
| `nsk:Booking:NoBookingInState` | no booking in the session |

### GET /api/nsk/v1/booking/payments · GET /api/nsk/v1/booking/payments/{paymentKey}

List of payments in the booking / one payment (400 `nsk:Payment:PaymentKeyNotFoundOnBooking` if not found).

### DELETE /api/nsk/v1/booking/payments/{paymentKey}

Removes a payment that has **not been committed** yet. Committed payments → 400 `nsk:Payment:NotRemovable` (refunds come in Phase 2).

---

## 8. Commit, status and retrieve — Booking

### POST /api/nsk/v3/booking — commit (first time)

Body optional:

```json
{
  "receivedBy": "Web",
  "comments": [ { "type": "Default", "text": "Window seat requested" } ],
  "notifyContacts": true,
  "distributionOption": "Email"
}
```

Response **201** `{ "data": null }` with `Location: /api/nsk/v1/booking`.

What happens, in order:

1. **Checks** — at least one journey; a primary contact `P` with a last name or email; every passenger has first and last name.
2. **Balance rule** — if `balanceDue` would stay above 0 and the booking can't be held (see `hold/available`) → `nsk:Booking:BalanceDueNotAllowed`.
3. A **record locator** is assigned (6 characters, no I, O, 0 or 1).
4. **Payments are authorised.** If a card is declined and the booking can't be held → 400 `nsk:Payment:Declined`. Nothing is saved, the seats stay held, and the payment shows status 4 (Declined). Add another payment and commit again.
5. **Seats are confirmed.**
6. The booking is **saved** and a `BookingCommitted` event is sent. If the contact chose email, an itinerary email goes out (see Mailpit).

Result: `info.status` 2 Confirmed when fully paid, otherwise 1 Hold with `hold.expiration`.

### PUT /api/nsk/v3/booking — commit changes again

After changing a committed booking in state (name, contact, new payment for a held booking), commit again with `PUT`.
Response **200** `{ "data": null }`.
If someone else changed the stored booking since you loaded it → **409** `nsk:Booking:ModifiedByAnotherSession`. Retrieve it again and redo your change.

### GET /api/nsk/v2/booking/status

Returns the booking in state (same as `GET /api/nsk/v1/booking`). Use it after commit to read `recordLocator`, `bookingKey`, `info.status` and `info.paidStatus`.

### GET /api/nsk/v2/booking/retrieve — customer retrieve (loads into state)

Query parameters: `RecordLocator` (required) plus **one** way to prove ownership:

| Strategy | Parameters |
|---|---|
| lastName | `LastName` — matches any passenger or contact |
| firstAndLastName | `FirstName` + `LastName` — the same person |
| email | `EmailAddress` — matches a contact |

```
GET /api/nsk/v2/booking/retrieve?RecordLocator=K7PM2Q&LastName=Dias
```

Response **200**: the booking, now in state (you can change it and `PUT` commit).
**Any** failure (not found, name mismatch, strategy not allowed) returns the same 400 `nsk:Booking:GeneralRestriction`, so record locators can't be guessed.
Retrieving replaces whatever booking was in the session (its uncommitted seats are released).

---

## 9. Agent-only endpoints

Log in first with `POST /api/auth/v1/token/user` (`DEF / agent01 / Password1!`).

| Method & path | What it does |
|---|---|
| `GET /api/nsk/v1/booking/retrieve/byRecordLocator/{recordLocator}` | Loads the booking into state with no name check. Unknown → 400 `nsk:Exceptions:InvalidKey` |
| `GET /api/nsk/v1/booking/retrieve/{bookingKey}` | Same, by `bookingKey` |
| `GET /api/nsk/v1/bookings/{recordLocator}` | **Stateless** read; does not touch the session. Unknown → 404 `{ "data": null }` |
| `POST /api/nsk/v6/booking/payments` with `CA` | Cash payment (approved immediately) |

Agents can also do everything a customer can.

---

## 10. Error codes

| Code | HTTP | Meaning |
|---|---|---|
| `core:Validation:NullRequest` | 400 | Body missing |
| `core:Validation:MalformedRequest` | 400 | Invalid JSON or wrong value type (e.g. unknown enum name) |
| `core:Validation:ResourceNotFound` | 400 | Unknown resource list / settings name |
| `core:Unauthorized` | 401 | No token, bad token or session expired / logged out |
| `core:Session:ConcurrentRequest` [DESIGN] | 409 | Another stateful call on the same session is still running |
| `core:ServiceUnavailable` [DESIGN] | 502/503/504 | A downstream service failed, is down or timed out |
| `core:Exception` | 500 | Unexpected error (details in Seq, search by `X-Correlation-Id`) |
| `nsk:Authorization:AgentOnly` [DESIGN] | 401 | Endpoint needs an agent session |
| `nsk:Authentication:InvalidCredentials` [DESIGN] | 401 | Wrong logon details |
| `nsk:Authentication:InvalidApplication` [DESIGN] | 400 | No anonymous user configured for the application |
| `nsk-server:PasswordExpired` | 400 | Password must be changed |
| `nsk:Exceptions:InvalidKey` | 400 | Key is malformed, unknown or expired |
| `nsk-server:InvalidDateRange` | 400 | Search date range too long or reversed |
| `nsk:Booking:InvalidStationCode` | 400 | Bad station code, or origin = destination |
| `nsk:Booking:NoBookingInState` | 400 | No booking in the session |
| `nsk:Booking:GeneralRestriction` | 400 | Retrieve failed (deliberately vague) |
| `nsk:Booking:NoJourneys` [DESIGN] | 400 | Commit without journeys |
| `nsk:Booking:MissingPrimaryContact` [DESIGN] | 400 | Commit without contact `P` (or it has no name/email) |
| `nsk:Booking:ContactTypeExists` / `ContactNotFound` [DESIGN] | 400 | Contact already exists / not found |
| `nsk:Booking:PassengerTypesMismatch` [DESIGN] | 400 | Sell with a different passenger mix |
| `nsk:Booking:JourneyAlreadySold` / `JourneyOverlap` / `JourneyNotChangeable` [DESIGN] | 400 | Journey rules |
| `nsk:Booking:BalanceDueNotAllowed` [DESIGN] | 400 | Commit with unpaid balance when hold isn't allowed |
| `nsk:Booking:ModifiedByAnotherSession` [DESIGN] | 409 | Stored booking changed since you loaded it |
| `nsk:Booking:FailedFareRestriction` | 400 | Booking needs re-pricing |
| `nsk:Booking:NotSupportedYet` [DESIGN] | 400 | Feature arrives in a later phase |
| `nsk:Availability:ClassNotAvailable` [DESIGN] | 400 | Seats no longer available, or held seats expired before commit |
| `nsk:Availability:MaxBookingPassengers` | 400 | More than 9 passengers |
| `nsk:Validation:UnknownSellCurrencyCode` | 400 | Currency not sold |
| `nsk:Validation:Invalid{First,Middle,Last}NameCharacters` | 400 | Name rules |
| `nsk:Validation:InvalidTitle` / `InvalidSuffix` | 400 | Longer than 6 characters |
| `nsk:Passenger:TypeNotFound` / `InvalidPassengerType` / `InvalidDateOfBirth` | 400 | Passenger type rules |
| `nsk:Passenger:FirstNameRequired` / `LastNameRequired` | 400 | Missing names at commit |
| `nsk:Contact:InvalidContactTypeCode` / `InvalidEmailAddress` [DESIGN] | 400 | Contact rules |
| `nsk:Currency:InvalidCurrencyCode` | 400 | Payment currency differs from booking |
| `nsk:Payment:…` | 400 | See [Payments](#7-payments--payment) |
| `nsk:Seat:UnitNotAvailable` [DESIGN] | 409 | Seat is held or sold to someone else — reload the seat map. Also at commit when a held seat was lost |
| `nsk:Seat:UnitNotAssignable` / `CabinMismatch` / `PassengerRestricted` / `NotAssigned` / `SeatMapNotFound` [DESIGN] | 400 | See [Seats](#14-seats--seat-service-phase-2) |

---

## 11. Enumerations

Sent and returned as numbers; you may also send the name.

| Enum | Values |
|---|---|
| BookingStatus | 0 Default (in state), 1 Hold, 2 Confirmed, 3 Closed, 4 HoldCanceled |
| PaidStatus | 0 UnderPaid, 1 PaidInFull, 2 OverPaid |
| PriceStatus | 0 Invalid, 1 Override, 2 Valid |
| ChannelType | 0 Default, 1 Direct, 2 Web, 3 Gds, 4 Api, 5 DigitalApi, 6 DigitalWeb, 7 Ndc |
| Gender | 0 XX, 1 Male, 2 Female |
| WeightCategory | 0 Unassigned, 1 Male, 2 Female, 3 Child |
| FlightType | 0 None, 1 NonStop, 2 Through, 3 Direct, 4 Connect, 5 All |
| SegmentStatus | 3 HeldPending (before commit), 6 HeldConfirmed (after commit) — full list in blueprint §11 |
| LegStatus | 0 Normal, 1 Closed, 2 Canceled, 3 Suspended |
| LiftStatus | 0 Default, 1 CheckedIn, 2 Boarded, 3 NoShow |
| ChargeType | 0 FarePrice, 1 Discount, 4 TravelFee, 5 Tax, 7 PromotionDiscount, 19 FareSurcharge |
| PaymentMethodType | 0 ExternalAccount (card), 1 PrePaid (cash), 2 AgencyAccount, 3 CustomerAccount, 4 Voucher, 5 Loyalty |
| BookingPaymentStatus | 0 New, 1 Received, 2 Pending, 3 Approved, 4 Declined, 6 PendingCustomerAction |
| AuthorizationStatus | 0 Unknown, 4 Approved, 5 Declined, 9 Voided |
| PhoneNumberType | 0 Other, 1 Home, 2 Work, 3 Mobile, 4 Fax |
| CommentType | 0 Default, 1 Itinerary, 2 Manifest, 3 Alert, 4 Archive |
| DistributionOption | 0 None, 1 Mail, 2 Email, 3 Fax |
| TaxesAndFeesRollupMode | 0 None, 1 Taxes, 2 TaxesAndFees |
| UserType | 1 Customer, 2 Agent, 3 Anonymous |
| SeatAvailability [DESIGN numbering] | 0 Unknown, 1 Reserved (sold), 2 Blocked, 3 HeldForAnotherSession, 4 HeldForThisSession (yours), 5 Open, 9 Restricted (not for this fare) |
| FeeType (seat) | 9 SeatFee — seat fees are `passengers[pk].fees[]` with `type` 9, `code` "SEAT" |

---

## 12. Demo data & test cards

**Flights** — carrier XX, daily, created for 30 days from the first start of Inventory:

| Flight | Route | Departs (local) | Aircraft |
|---|---|---|---|
| XX101 / XX102 | CMB→DXB / DXB→CMB | 08:30 / 14:00 | A321 |
| XX201 / XX202 | CMB→SIN / SIN→CMB | 23:55 / 08:00 | A320 |
| XX301 / XX302 | CMB→BKK / BKK→CMB | 01:10 / 07:30 | A320 |
| XX401 / XX402 | CMB→MLE / MLE→CMB | 10:00 / 12:30 | A320 |
| XX501 / XX502 | CMB→DEL / DEL→CMB | 09:15 / 14:30 | A321 |
| XX601 / XX602 | CMB→KUL / KUL→CMB | 13:00 / 18:30 | A320 |
| XX701 / XX702 | DXB→LHR / LHR→DXB | 15:30 / 21:00 | A330 |
| XX801 / XX802 | CMB→JAF / JAF→CMB (domestic) | 07:00 / 09:00 | ATR72 |

CMB→LHR and LHR→CMB are sold as **connections via DXB**.

**Fares** (one-way, USD, CMB⇄DXB example; other markets scale with their base fare):

| Class | Product | Fare | Class | Product | Fare |
|---|---|---|---|---|---|
| L | EC | 180 | M | ES | 315 |
| Q | EC | 216 | B | EF | 378 |
| H | ES | 261 | Y | EF | 468 |
| D | BZ | 720 | C / J | BZ | 900 / 1080 |

Market base fares: CMB–DXB 180, CMB–SIN 160, CMB–BKK 150, CMB–MLE 90, CMB–DEL 140, CMB–KUL 150, DXB–LHR 420, CMB–JAF 45.

**Discounts and taxes:** CHD 25 % off the fare. LK embarkation levy 60 (international departures from Sri Lanka), UAE 20, UK APD 95 (ADT only), fuel surcharge YQ 25 per segment, LK VAT 8 % on domestic flights. Infants are exempt from the levies.
Example: 1 adult CMB→DXB in L = 180 + 60 + 25 = **265 USD**.

**Users:**

| Type | Domain | Username | Password |
|---|---|---|---|
| Anonymous | – | (use `token/anonymous`) | – |
| Agent | DEF | agent01 | Password1! |
| Customer | WWW | customer@demo.test | Password1! |

**Test cards** (any future expiry, any 3-digit CVV):

| Card | Result at commit |
|---|---|
| 4111111111111111 | approved |
| 5555555555554444 | approved (MasterCard, use `MC`) |
| 4000000000000002 | declined — "Do not honour" |
| 4000000000009995 | declined — "Insufficient funds" |

---

## 13. Internal & operational endpoints

**Health** (every service, not via gateway): `/health` (all checks), `/health/live` (process only), `/health/ready` (SQL, Redis).
The gateway itself has `/health`.

**Discovery** (Identity, via gateway): `GET /.well-known/openid-configuration`, `GET /.well-known/jwks.json`.

**Internal service-to-service APIs** — never routed by the gateway. They use the caller's forwarded token.
Listed here for debugging with Swagger only:

| Service | Endpoint | Used by |
|---|---|---|
| Inventory | `POST /internal/inventory/segments/search`, `POST /internal/inventory/holds`, `/holds/confirm`, `/holds/release` | Shopping, Booking |
| Pricing | `POST /internal/pricing/journeys`, `POST /internal/pricing/selection` | Shopping, Booking |
| Shopping | `GET /internal/shopping/offers/{fareAvailabilityKey}` | Booking |
| Booking | `GET /internal/booking/draft`, `POST /internal/booking/draft/payments`, `DELETE /internal/booking/draft/payments/{key}` | Payment |
| Payment | `POST /internal/payments/authorize`, `POST /internal/payments/void` | Booking (commit) |
| Booking | `GET /internal/booking/draft/booking`, `POST /internal/booking/draft/seats`, `POST /internal/booking/draft/seats/remove` | Seat |
| Seat | `POST /internal/seats/confirm`, `/finalize`, `/release`, `/revert` | Booking (commit, reset, retrieve) |

---

## 14. Seats — Seat service (Phase 2)

The Seat service (port 5110, gateway cluster `seat`) owns seat maps, seat inventory and seat prices. It works on the
**booking in state**: sell first, then pick seats, then pay and commit. Seat selection is optional.

### GET /api/nsk/v3/booking/seatmaps/journey/{journeyKey} · GET /api/nsk/v3/booking/seatmaps/segment/{segmentKey}

`v1` and `v2` are accepted as aliases. Returns one item **per flight leg** (a connection has two):

```json
{
  "data": [
    {
      "journeyKey": "…", "segmentKey": "…", "legKey": "…",
      "seatMap": {
        "name": "Airbus A321", "equipmentType": "321", "marketingCode": "XX 101",
        "departureStation": "CMB", "arrivalStation": "DXB", "availableUnits": 170, "fareCompartment": "Y",
        "decks": { "1": { "number": 1, "availableUnits": 170, "compartments": {
          "C": { "compartmentDesignator": "C", "sequence": 1, "length": 3, "width": 5, "availableUnits": 0, "units": [ … ] },
          "Y": { "compartmentDesignator": "Y", "sequence": 2, "length": 30, "width": 7, "availableUnits": 170, "units": [
            { "unitKey": "NjM5…", "designator": "12A", "compartmentDesignator": "Y", "assignable": true, "availability": 5,
              "group": 1, "travelClassCode": "Y", "x": 0, "y": 12, "type": "Seat",
              "properties": [ { "code": "WINDOW", "value": "true" } ], "restrictedPassengerTypes": [] }
          ] }
        } } }
      },
      "fees": { "MCFBRFQ-": { "passengerKey": "MCFBRFQ-", "groups": {
        "1": { "group": 1, "fees": [ { "type": 9, "code": "SEAT", "serviceCharges": [ { "type": 6, "code": "SEAT", "amount": 8, "currencyCode": "USD" } ] } ] },
        "2": { "group": 2, "fees": [ … 15 … ] }, "3": { "group": 3, "fees": [ … 25 … ] }, "4": { "group": 4, "fees": [ … 0 … ] } } } },
      "propertyLookup": { "WINDOW": "Window seat", "AISLE": "Aisle seat", "EXITROW": "Emergency exit row", "LEGROOM": "Extra legroom", "PREFERRED": "Front of the cabin" },
      "groupLookup": { "1": "Standard", "2": "Preferred", "3": "Extra legroom", "4": "Business" }
    }
  ]
}
```

How to read it:

- Draw the cabin from `x` / `y` (x counts seat columns plus aisles; `y` counts rows, with an empty row between cabins).
- `availability` is for **your** session: 5 Open, 4 yours (held or confirmed for this booking), 3 held by another
  customer who hasn't paid yet, 1 sold, 2 blocked, 9 not available to your fare (e.g. Business seats on an Economy fare).
- Price of a seat for a passenger = `fees[passengerKey].groups[unit.group]` (sum of `serviceCharges`). It is per leg.
- `restrictedPassengerTypes` lists passenger types that may not sit there (exit rows: CHD, INF).

### POST /api/nsk/v2/booking/passengers/{passengerKey}/seats/{unitKey}

Body optional: `{ "waive": false }` (`waive: true` = no seat fee, agents only). Response **201**, `Location: /api/nsk/v1/booking/passengers/{passengerKey}/seats`.

- Puts the seat on `journeys[].segments[].passengerSegment[pk].seats[]` and, if it costs money, a seat fee on
  `passengers[pk].fees[]` (type 9 SeatFee). `breakdown.passengerTotals.seats` and `balanceDue` include it.
- A passenger has **one seat per leg**: assigning another seat replaces the first one (its hold and fee are removed).
- Assigning the seat the passenger already has does nothing.
- The seat is **held** for your session until you commit, log out, or the session expires (15 idle minutes).

### DELETE /api/nsk/v1/booking/passengers/{passengerKey}/seats/{unitKey}

Removes the seat and its fee. `v2` accepted. 400 `nsk:Seat:NotAssigned` if the passenger doesn't have that seat.

### GET /api/nsk/v1/booking/passengers/{passengerKey}/seats

The passenger's seats on all legs (`PassengerSeat[]`: `unitDesignator`, `compartmentDesignator`, `departureStation`, `arrivalStation`, `unitKey`, `seatInformation.propertyList`).

### Rules

| Rule | Error |
|---|---|
| Seat sold or held by another session | **409** `nsk:Seat:UnitNotAvailable` — reload the seat map |
| Seat already given to another passenger of the same booking | **409** `nsk:Seat:UnitNotAvailable` |
| Blocked seat or no such seat on this aircraft | 400 `nsk:Seat:UnitNotAssignable` |
| Business seat on an Economy fare (or the reverse) | 400 `nsk:Seat:CabinMismatch` |
| Child in an emergency exit row | 400 `nsk:Seat:PassengerRestricted` |
| `waive` without an agent session | 401 `nsk:Authorization:AgentOnly` |
| Two seat calls at once on the same session | 409 `core:Session:ConcurrentRequest` |

**No double booking.** Every taken seat is a row in the Seat database with a **unique index on (flight leg, seat)**. A
seat request is an `INSERT`; when two customers click the same seat at the same moment, the database accepts one and
rejects the other, which gets 409 `nsk:Seat:UnitNotAvailable`. Holds belong to the session: logout, session expiry
(`SessionExpired` event), `booking/reset`, removing the journey or retrieving another booking release them, and a
sweeper frees holds of sessions that no longer exist.

**Commit.** `POST`/`PUT v3/booking` confirms the booking's seats in the Seat service before inventory is confirmed. If a
held seat was lost in the meantime (session expired), the commit fails with **409** `nsk:Seat:UnitNotAvailable`, any card
authorised in that commit is voided, and nothing is saved — pick the seat again and commit again. For a committed
booking (after retrieve), a changed seat keeps the old seat until the change is committed; then the old seat is freed.
A seat fee added to a confirmed booking leaves a balance due: pay it before committing (or the booking goes on hold
when holding is allowed).

**Prices** (USD, per passenger per leg; demo data in `tools/Pss.SeedData/DemoCarrier.cs`):

| Group | Seats | Price |
|---|---|---|
| 1 Standard | most economy seats | 8 |
| 2 Preferred | first 5 economy rows (3 on the ATR) | 15 |
| 3 Extra legroom | emergency exit rows (adults only) | 25 |
| 4 Business | business cabin | 0 |

Flex (`EF`) and Business (`BZ`) fares get every seat free.

**Seat maps** (assignable seats = inventory capacity): A320 rows 1–30 3-3 (exit 12–13) · A321 Business rows 1–3 2-2,
Economy rows 4–33 3-3 (exit 16–17; 33A/B/E/F blocked) · A330-300 Business rows 1–7 1-2-1, Economy rows 10–39 3-3-3
(exit 24; 39K blocked) · ATR 72 rows 1–18 2-2 (exit 12; 1A/1C blocked).

---

*Not implemented yet (see `CLAUDE.md` → Next steps):* infants, SSRs/bundles/baggage, promotions, fare rules,
change/cancel/refund, booking search, queues, check-in and boarding passes, 3-D Secure/DCC, vouchers, customer profiles.
Calling a route that belongs to a service that doesn't exist yet returns **502** from the gateway.

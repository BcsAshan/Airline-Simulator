# Generates postman/Pss.postman_collection.json and postman/Pss-Local.postman_environment.json.
# Usage (from Project/):  python tools/postman/generate_postman.py postman
# Edit the folder definitions below when endpoints are added, then re-run and re-import into Postman.
import json, uuid, os, sys
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

def uid(s): return str(uuid.uuid5(uuid.NAMESPACE_URL, "pss-postman/" + s))

def req(name, method, path, body=None, tests=None, auth=None, headers=None, desc=None, query=None):
    url_raw = "{{baseUrl}}" + path
    path_parts = path.split("?")[0].strip("/").split("/")
    url = {"raw": url_raw + ("" if not query else "?" + "&".join(f"{k}={v}" for k, v in query)),
           "host": ["{{baseUrl}}"], "path": path_parts}
    if query: url["query"] = [{"key": k, "value": v} for k, v in query]
    h = [{"key": "Content-Type", "value": "application/json"}] if body is not None else []
    for k, v in (headers or []): h.append({"key": k, "value": v})
    r = {"method": method, "header": h, "url": url}
    if body is not None:
        r["body"] = {"mode": "raw", "raw": json.dumps(body, indent=2) if not isinstance(body, str) else body,
                     "options": {"raw": {"language": "json"}}}
    if auth == "none": r["auth"] = {"type": "noauth"}
    elif auth: r["auth"] = {"type": "bearer", "bearer": [{"key": "token", "value": "{{" + auth + "}}", "type": "string"}]}
    if desc: r["description"] = desc
    item = {"name": name, "request": r}
    if tests: item["event"] = [{"listen": "test", "script": {"type": "text/javascript", "exec": tests.strip("\n").split("\n")}}]
    return item

def folder(name, items, desc=None, auth=None):
    f = {"name": name, "item": items}
    if desc: f["description"] = desc
    if auth: f["auth"] = {"type": "bearer", "bearer": [{"key": "token", "value": "{{" + auth + "}}", "type": "string"}]}
    return f

def status(code): return f'pm.test("HTTP {code}", () => pm.response.to.have.status({code}));\n'

def error(code, http=400): return status(http) + f'''pm.test("error code {code}", () => pm.expect(pm.response.json().errors[0].code).to.eql("{code}"));
'''

# ---------------------------------------------------------------- reusable scripts
T_TOKEN = lambda var="token": status(201) + f'''const d = pm.response.json().data;
pm.collectionVariables.set("{var}", d.token);
pm.test("token returned", () => pm.expect(d.token).to.be.a("string"));
'''

def T_SEARCH(prefix="", market="{{origin}}|{{destination}}", result=0):
    return status(200) + "{\n" + f'''// Picks the first journey that still has a fare and stores its keys.
const d = pm.response.json().data;
const market = pm.variables.replaceIn("{market}");
const journeys = d.results[{result}].trips.flatMap(t => t.journeysAvailableByMarket[market] || []);
const j = journeys.find(x => x.fares.length > 0);
pm.test("a bookable journey was found for " + market, () => pm.expect(j).to.exist);
if (j) {{
  pm.collectionVariables.set("{prefix}journeyKey", j.journeyKey);
  pm.collectionVariables.set("{prefix}fareAvailabilityKey", j.fares[0].fareAvailabilityKey);
  const fare = d.faresAvailable[j.fares[0].fareAvailabilityKey];
  console.log("Selected", j.segments.map(s => s.identifier.carrierCode + s.identifier.identifier).join("+"),
              j.designator.departure, "class", fare.fares.map(f => f.classOfService).join("/"), "fareTotal", fare.totals.fareTotal);
}}
''' + "}\n"

T_SELL = status(200) + '''const b = pm.response.json().data;
pm.collectionVariables.set("balanceDue", b.breakdown.balanceDue);
const keys = Object.keys(b.passengers);
keys.forEach((k, i) => pm.collectionVariables.set("passengerKey" + (i + 1), k));
pm.collectionVariables.set("passengerKey", keys[0]);
pm.test("booking is in state (status 0)", () => pm.expect(b.info.status).to.eql(0));
pm.test("balance due > 0", () => pm.expect(b.breakdown.balanceDue).to.be.above(0));
console.log("Balance due", b.breakdown.balanceDue, b.currencyCode, "passengers", keys);
'''

T_BALANCE = status(200) + '''const b = pm.response.json().data;
pm.collectionVariables.set("balanceDue", b.breakdown.balanceDue);
const keys = Object.keys(b.passengers);
keys.forEach((k, i) => pm.collectionVariables.set("passengerKey" + (i + 1), k));
pm.collectionVariables.set("passengerKey", keys[0]);
'''

T_PAYMENT = status(201) + '''const loc = pm.response.headers.get("Location");
pm.test("Location header", () => pm.expect(loc).to.include("/api/nsk/v1/booking/payments/"));
pm.collectionVariables.set("paymentKey", loc.split("/").pop());
'''

T_COMMIT = status(201) + 'pm.test("Location /api/nsk/v1/booking", () => pm.expect(pm.response.headers.get("Location")).to.eql("/api/nsk/v1/booking"));\n'

T_STATUS = status(200) + '''const b = pm.response.json().data;
pm.collectionVariables.set("recordLocator", b.recordLocator);
pm.collectionVariables.set("bookingKey", b.bookingKey);
pm.test("record locator assigned", () => pm.expect(b.recordLocator).to.match(/^[A-Z2-9]{6}$/));
pm.test("confirmed (status 2)", () => pm.expect(b.info.status).to.eql(2));
pm.test("paid in full", () => pm.expect(b.info.paidStatus).to.eql(1));
console.log("Record locator", b.recordLocator);
'''

T_OK = status(200)
T_LOGOUT = status(200)

# ---------------------------------------------------------------- bodies
def search_simple(origin="{{origin}}", destination="{{destination}}", date="{{travelDate}}", pax=None, fares=4):
    return {"origin": origin, "destination": destination, "beginDate": date,
            "passengers": {"types": pax or [{"type": "ADT", "count": 1}]},
            "codes": {"currencyCode": "USD"}, "numberOfFaresPerJourney": fares}

SELL_1ADT = {"keys": [{"journeyKey": "{{journeyKey}}", "fareAvailabilityKey": "{{fareAvailabilityKey}}"}],
             "passengers": {"types": [{"type": "ADT", "count": 1}]}, "currencyCode": "USD"}

def pax_body(first, last, dob, gender="Male", title="MR", nat="LK"):
    return {"name": {"first": first, "last": last, "title": title},
            "info": {"nationality": nat, "residentCountry": nat, "gender": gender, "dateOfBirth": dob}}

CONTACT = {"contactTypeCode": "P", "name": {"first": "{{firstName}}", "last": "{{lastName}}", "title": "MR"},
           "emailAddress": "{{email}}", "phoneNumbers": [{"type": "Mobile", "number": "+94770000000"}],
           "address": {"lineOne": "1 Galle Road", "city": "Colombo", "countryCode": "LK", "postalCode": "00300"},
           "distributionOption": "Email"}

def card(pan="4111111111111111", code="VI"):
    return ('{\n  "paymentMethodCode": "' + code + '",\n  "amount": {{balanceDue}},\n  "currencyCode": "USD",\n'
            '  "paymentFields": {\n    "ACCTNO": "' + pan + '",\n    "EXPDAT": "12/2030",\n'
            '    "CC::AccountHolderName": "{{firstName}} {{lastName}}",\n    "CC::VerificationCode": "123"\n  }\n}')

COMMIT = {"receivedBy": "Postman", "comments": [{"type": "Default", "text": "Created from the Postman collection"}]}

TOKEN_REQ = lambda name="Anonymous token": req(name, "POST", "/api/auth/v1/token/anonymous", {"applicationName": "dotREZWeb"}, T_TOKEN(), auth="none",
    desc="Creates an anonymous session. The test script stores the token in {{token}}; every other request sends it as Bearer.")

# ---------------------------------------------------------------- folders
f_health = folder("00 Health", [
    req("Gateway health", "GET", "/health", tests=status(200), auth="none"),
], "Is the gateway up? (Service health endpoints are on each service port: /health, /health/live, /health/ready.)")

f_auth = folder("01 Auth & session", [
    TOKEN_REQ(),
    req("Token info", "GET", "/api/auth/v1/token", tests=T_OK + 'pm.test("anonymous user type 3", () => pm.expect(pm.response.json().data.userType).to.eql(3));\n'),
    req("Refresh token (keep alive)", "PUT", "/api/auth/v1/token", tests=T_OK + 'pm.collectionVariables.set("token", pm.response.json().data.token);\n'),
    req("Agent login", "POST", "/api/auth/v1/token/user",
        {"applicationName": "dotREZWeb", "credentials": {"domain": "DEF", "username": "agent01", "password": "Password1!"}},
        T_TOKEN("agentToken"), auth="none", desc="Stores {{agentToken}} (used by folder 07)."),
    req("Customer login", "POST", "/api/auth/v1/token/user",
        {"credentials": {"domain": "WWW", "username": "customer@demo.test", "password": "Password1!"}},
        T_TOKEN("customerToken"), auth="none"),
    req("Legacy token (POST /api/nsk/v2/token)", "POST", "/api/nsk/v2/token", {}, T_TOKEN("legacyToken"), auth="none",
        desc="Navitaire legacy route used by their Postman collections. Empty body = anonymous."),
    req("Wrong password → 401", "POST", "/api/auth/v1/token/user",
        {"credentials": {"domain": "DEF", "username": "agent01", "password": "wrong"}},
        error("nsk:Authentication:InvalidCredentials", 401), auth="none"),
    req("Logout", "DELETE", "/api/auth/v1/token", tests=T_LOGOUT,
        desc="Ends the session, releases held seats. Run 'Anonymous token' again before other folders."),
], "Tokens are handles to a server-side session that expires after 15 idle minutes.")

f_ref = folder("02 Reference data", [
    TOKEN_REQ(),
    req("Stations", "GET", "/api/nsk/v1/resources/stations",
        tests=T_OK + 'pm.collectionVariables.set("stationsEtag", pm.response.headers.get("ETag"));\npm.test("has ETag", () => pm.expect(pm.response.headers.get("ETag")).to.be.a("string"));\n'),
    req("Stations again with If-None-Match → 304", "GET", "/api/nsk/v1/resources/stations",
        headers=[("If-None-Match", "{{stationsEtag}}")], tests=status(304)),
    req("Station CMB", "GET", "/api/nsk/v1/resources/stations/CMB", tests=T_OK),
    req("Markets (v2)", "GET", "/api/nsk/v2/resources/markets", tests=T_OK),
    req("Countries (v2)", "GET", "/api/nsk/v2/resources/countries", tests=T_OK),
    req("Country LK (v2)", "GET", "/api/nsk/v2/resources/countries/LK", tests=T_OK),
    req("Currencies (v2)", "GET", "/api/nsk/v2/resources/currencies", tests=T_OK),
    req("Passenger types", "GET", "/api/nsk/v1/resources/passengerTypes", tests=T_OK),
    req("Titles", "GET", "/api/nsk/v1/resources/titles", tests=T_OK),
    req("Document types", "GET", "/api/nsk/v1/resources/documentTypes", tests=T_OK),
    req("Contact types", "GET", "/api/nsk/v1/resources/contactTypes", tests=T_OK),
    req("Settings booking/passenger", "GET", "/api/nsk/v1/settings/booking/passenger", tests=T_OK),
    req("Settings booking/general", "GET", "/api/nsk/v1/settings/booking/general", tests=T_OK),
    req("Station local time CMB", "GET", "/api/nsk/v1/utilities/stationLocalTime/CMB", tests=T_OK),
])

f_avail = folder("03 Availability", [
    TOKEN_REQ(),
    req("Simple search CMB→DXB (1 ADT)", "POST", "/api/nsk/v4/availability/search/simple", search_simple(), T_SEARCH()),
    req("Simple search over 3 days", "POST", "/api/nsk/v4/availability/search/simple",
        {**search_simple(), "endDate": "{{returnDate}}"}, T_OK),
    req("Connection search CMB→LHR (via DXB)", "POST", "/api/nsk/v4/availability/search/simple",
        search_simple("CMB", "LHR"), T_SEARCH("cnx", "CMB|LHR") +
        'pm.test("connection has 2 segments", () => { const d = pm.response.json().data; const j = d.results[0].trips.flatMap(t => t.journeysAvailableByMarket["CMB|LHR"] || [])[0]; pm.expect(j.segments.length).to.eql(2); pm.expect(j.flightType).to.eql(4); });\n'),
    req("Non-stop only filter", "POST", "/api/nsk/v4/availability/search/simple",
        {**search_simple("CMB", "LHR"), "filters": {"maxConnections": 0}}, T_OK),
    req("Round trip CMB⇄SIN (full search, 2 ADT + 1 CHD)", "POST", "/api/nsk/v4/availability/search",
        {"criteria": [
            {"stations": {"originStationCodes": ["CMB"], "destinationStationCodes": ["SIN"]}, "dates": {"beginDate": "{{travelDate}}"}},
            {"stations": {"originStationCodes": ["SIN"], "destinationStationCodes": ["CMB"]}, "dates": {"beginDate": "{{returnDate}}"}}],
         "passengers": {"types": [{"type": "ADT", "count": 2}, {"type": "CHD", "count": 1}]},
         "codes": {"currencyCode": "USD"}, "numberOfFaresPerJourney": 4},
        T_SEARCH("out", "CMB|SIN", 0) + T_SEARCH("ret", "SIN|CMB", 1).replace(status(200), "")),
    req("Bad station → 400", "POST", "/api/nsk/v4/availability/search/simple",
        search_simple("CM1", "DXB"), error("nsk:Booking:InvalidStationCode")),
], "Search results hold offers for 30 minutes. Test scripts store journeyKey/fareAvailabilityKey for selling.")

f_flow = folder("04 Booking flow — 1 adult, card", [
    TOKEN_REQ("1. Anonymous token"),
    req("2. Search CMB→DXB", "POST", "/api/nsk/v4/availability/search/simple", search_simple(), T_SEARCH()),
    req("3. Trip sell", "POST", "/api/nsk/v4/trip/sell", SELL_1ADT, T_SELL,
        desc="Creates the booking in state and holds seats until the session ends."),
    req("4. Get booking in state", "GET", "/api/nsk/v1/booking", tests=T_OK),
    req("5. Update passenger (PUT v3)", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey}}",
        pax_body("{{firstName}}", "{{lastName}}", "1990-05-14"), T_OK),
    req("6. Add primary contact", "POST", "/api/nsk/v1/booking/contacts", CONTACT,
        status(201) + 'pm.test("Location", () => pm.expect(pm.response.headers.get("Location")).to.eql("/api/nsk/v1/booking/contacts/P"));\n'),
    req("7. Hold available?", "GET", "/api/nsk/v2/booking/hold/available", tests=T_OK),
    req("8. Payment methods", "GET", "/api/nsk/v2/booking/payments/available",
        tests=T_OK + 'pm.test("Visa offered", () => pm.expect(pm.response.json().data).to.have.property("VI"));\n'),
    req("9. Add card payment (full balance)", "POST", "/api/nsk/v6/booking/payments", card(), T_PAYMENT),
    req("10. Get payments", "GET", "/api/nsk/v1/booking/payments",
        tests=T_OK + 'pm.test("card is masked", () => pm.expect(pm.response.json().data[0].details.accountNumber).to.match(/^X+\\d{4}$/));\n'),
    req("11. Commit (POST v3/booking)", "POST", "/api/nsk/v3/booking", COMMIT, T_COMMIT),
    req("12. Booking status", "GET", "/api/nsk/v2/booking/status", tests=T_STATUS),
    req("13. Logout", "DELETE", "/api/auth/v1/token", tests=T_LOGOUT,
        desc="Itinerary email: open Mailpit http://localhost:8025"),
], "The full Navitaire happy path (blueprint §12.1). Run the folder with the Collection Runner or request by request.")

f_family = folder("05 Round trip family — 2 ADT + 1 CHD", [
    TOKEN_REQ("1. Anonymous token"),
    req("2. Round trip search CMB⇄SIN", "POST", "/api/nsk/v4/availability/search",
        {"criteria": [
            {"stations": {"originStationCodes": ["CMB"], "destinationStationCodes": ["SIN"]}, "dates": {"beginDate": "{{travelDate}}"}},
            {"stations": {"originStationCodes": ["SIN"], "destinationStationCodes": ["CMB"]}, "dates": {"beginDate": "{{returnDate}}"}}],
         "passengers": {"types": [{"type": "ADT", "count": 2}, {"type": "CHD", "count": 1}]},
         "codes": {"currencyCode": "USD"}, "numberOfFaresPerJourney": 4},
        T_SEARCH("out", "CMB|SIN", 0) + T_SEARCH("ret", "SIN|CMB", 1).replace(status(200), "")),
    req("3. Sell both journeys", "POST", "/api/nsk/v4/trip/sell",
        {"keys": [{"journeyKey": "{{outjourneyKey}}", "fareAvailabilityKey": "{{outfareAvailabilityKey}}"},
                  {"journeyKey": "{{retjourneyKey}}", "fareAvailabilityKey": "{{retfareAvailabilityKey}}"}],
         "passengers": {"types": [{"type": "ADT", "count": 2}, {"type": "CHD", "count": 1}]}, "currencyCode": "USD"},
        T_SELL + 'pm.test("2 journeys, 3 passengers", () => { const b = pm.response.json().data; pm.expect(b.journeys.length).to.eql(2); pm.expect(Object.keys(b.passengers).length).to.eql(3); });\n'),
    req("4. Adult 1", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey1}}", pax_body("{{firstName}}", "{{lastName}}", "1990-05-14"), T_OK),
    req("5. Adult 2", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey2}}", pax_body("Nimali", "{{lastName}}", "1992-09-02", "Female", "MRS"), T_OK),
    req("6. Child (must be 2–11 on travel date)", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey3}}", pax_body("Kavin", "{{lastName}}", "2018-03-01", "Male", "MSTR"), T_OK),
    req("7. Add primary contact", "POST", "/api/nsk/v1/booking/contacts", CONTACT, status(201)),
    req("8. Get booking (balance after CHD discount)", "GET", "/api/nsk/v1/booking", tests=T_BALANCE),
    req("9. Pay with MasterCard", "POST", "/api/nsk/v6/booking/payments", card("5555555555554444", "MC"), T_PAYMENT),
    req("10. Commit", "POST", "/api/nsk/v3/booking", COMMIT, T_COMMIT),
    req("11. Booking status", "GET", "/api/nsk/v2/booking/status", tests=T_STATUS),
    req("12. Logout", "DELETE", "/api/auth/v1/token", tests=T_LOGOUT),
])

f_trip = folder("06 One-shot booking — POST v5/trip", [
    TOKEN_REQ("1. Anonymous token"),
    req("2. Search CMB→MLE", "POST", "/api/nsk/v4/availability/search/simple", search_simple("CMB", "MLE"), T_SEARCH("", "CMB|MLE")),
    req("3. Trip (sell + passenger + contact in one call)", "POST", "/api/nsk/v5/trip",
        {"journeys": {"keys": [{"journeyKey": "{{journeyKey}}", "fareAvailabilityKey": "{{fareAvailabilityKey}}"}]},
         "passengers": [{"passenger": {"passengerTypeCode": "ADT", **pax_body("{{firstName}}", "{{lastName}}", "1990-05-14")}}],
         "contact": {k: v for k, v in CONTACT.items() if k != "contactTypeCode"},
         "currencyCode": "USD"},
        T_SELL.replace('pm.test("booking is in state (status 0)", () => pm.expect(b.info.status).to.eql(0));\n', '')),
    req("4. Add card payment", "POST", "/api/nsk/v6/booking/payments", card(), T_PAYMENT),
    req("5. Commit", "POST", "/api/nsk/v3/booking", COMMIT, T_COMMIT),
    req("6. Booking status", "GET", "/api/nsk/v2/booking/status", tests=T_STATUS),
    req("7. Logout", "DELETE", "/api/auth/v1/token", tests=T_LOGOUT),
])

f_manage = folder("07 Manage booking (customer)", [
    TOKEN_REQ("1. New anonymous token"),
    req("2. Retrieve by record locator + last name", "GET", "/api/nsk/v2/booking/retrieve",
        query=[("RecordLocator", "{{recordLocator}}"), ("LastName", "{{lastName}}")],
        tests=T_OK + 'pm.test("same booking", () => pm.expect(pm.response.json().data.recordLocator).to.eql(pm.collectionVariables.get("recordLocator")));\n',
        desc="Run folder 04/05/06 first so {{recordLocator}} is set."),
    req("3. Retrieve by email", "GET", "/api/nsk/v2/booking/retrieve",
        query=[("RecordLocator", "{{recordLocator}}"), ("EmailAddress", "{{email}}")], tests=T_OK),
    req("4. Change contact phone (PUT contacts/primary)", "PUT", "/api/nsk/v1/booking/contacts/primary",
        {**CONTACT, "phoneNumbers": [{"type": "Mobile", "number": "+94771234567"}]}, T_OK),
    req("5. Re-commit (PUT v3/booking) → 200", "PUT", "/api/nsk/v3/booking", {"receivedBy": "Postman manage"}, T_OK),
    req("6. Status after change", "GET", "/api/nsk/v2/booking/status",
        tests=T_OK + 'pm.test("history has BookingModified", () => pm.expect(pm.response.json().data.history.map(h => h.event)).to.include("BookingModified"));\n'),
    req("7. Retrieve with wrong name → GeneralRestriction", "GET", "/api/nsk/v2/booking/retrieve",
        query=[("RecordLocator", "{{recordLocator}}"), ("LastName", "Nobody")], tests=error("nsk:Booking:GeneralRestriction")),
    req("8. Logout", "DELETE", "/api/auth/v1/token", tests=T_LOGOUT),
])

f_agent = folder("08 Agent", [
    req("1. Agent login", "POST", "/api/auth/v1/token/user",
        {"applicationName": "dotREZWeb", "credentials": {"domain": "DEF", "username": "agent01", "password": "Password1!"}},
        T_TOKEN("agentToken"), auth="none"),
    req("2. Stateless booking read (GET v1/bookings/{rl})", "GET", "/api/nsk/v1/bookings/{{recordLocator}}", tests=T_OK,
        desc="Needs {{recordLocator}} from an earlier booking folder."),
    req("3. Retrieve into state by record locator", "GET", "/api/nsk/v1/booking/retrieve/byRecordLocator/{{recordLocator}}", tests=T_OK),
    req("4. Retrieve into state by booking key", "GET", "/api/nsk/v1/booking/retrieve/{{bookingKey}}", tests=T_OK),
    req("5. Unknown record locator → 404", "GET", "/api/nsk/v1/bookings/ZZZZZZ", tests=status(404)),
    req("6. Search CMB→DEL", "POST", "/api/nsk/v4/availability/search/simple", search_simple("CMB", "DEL"), T_SEARCH("", "CMB|DEL")),
    req("7. Trip sell (replaces the retrieved booking in state)", "POST", "/api/nsk/v4/trip/sell", SELL_1ADT, T_SELL),
    req("8. Passenger", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey}}", pax_body("Walk", "Incustomer", "1985-01-20"), T_OK),
    req("9. Contact", "POST", "/api/nsk/v1/booking/contacts",
        {**CONTACT, "name": {"first": "Walk", "last": "Incustomer"}, "emailAddress": "walkin@example.com"}, status(201)),
    req("10. Payment methods (agent sees CA cash)", "GET", "/api/nsk/v2/booking/payments/available",
        tests=T_OK + 'pm.test("cash offered to agents", () => pm.expect(pm.response.json().data).to.have.property("CA"));\n'),
    req("11. Cash payment", "POST", "/api/nsk/v6/booking/payments",
        '{\n  "paymentMethodCode": "CA",\n  "amount": {{balanceDue}},\n  "currencyCode": "USD"\n}', T_PAYMENT),
    req("12. Commit", "POST", "/api/nsk/v3/booking", {"receivedBy": "agent01"}, T_COMMIT),
    req("13. Booking status", "GET", "/api/nsk/v2/booking/status", tests=T_STATUS),
    req("14. Logout agent", "DELETE", "/api/auth/v1/token", tests=T_LOGOUT),
], "All requests in this folder use {{agentToken}}.", auth="agentToken")

f_neg = folder("09 Error cases", [
    req("No token → 401", "GET", "/api/nsk/v1/booking", tests=error("core:Unauthorized", 401), auth="none"),
    TOKEN_REQ("Anonymous token"),
    req("No booking in state → 400", "GET", "/api/nsk/v1/booking", tests=error("nsk:Booking:NoBookingInState")),
    req("Agent endpoint with anonymous token → 401", "GET", "/api/nsk/v1/bookings/ABCDEF", tests=error("nsk:Authorization:AgentOnly", 401)),
    req("Sell with an invalid key → InvalidKey", "POST", "/api/nsk/v4/trip/sell",
        {"keys": [{"journeyKey": "bad-key", "fareAvailabilityKey": "bad-key"}], "passengers": {"types": [{"type": "ADT", "count": 1}]}},
        error("nsk:Exceptions:InvalidKey")),
    req("Search for the declined-card test", "POST", "/api/nsk/v4/availability/search/simple", search_simple("CMB", "BKK"), T_SEARCH("", "CMB|BKK")),
    req("Sell", "POST", "/api/nsk/v4/trip/sell", SELL_1ADT, T_SELL),
    req("Invalid name characters → 400", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey}}",
        pax_body("J@ne", "Doe", "1990-01-01", "Female", "MS"), error("nsk:Validation:InvalidFirstNameCharacters")),
    req("Adult too young → InvalidDateOfBirth", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey}}",
        pax_body("Tiny", "Tim", "2020-01-01"), error("nsk:Passenger:InvalidDateOfBirth")),
    req("Commit without names/contact → 400", "POST", "/api/nsk/v3/booking", {}, status(400)),
    req("Passenger", "PUT", "/api/nsk/v3/booking/passengers/{{passengerKey}}", pax_body("Decline", "Test", "1980-01-01"), T_OK),
    req("Contact", "POST", "/api/nsk/v1/booking/contacts", {**CONTACT, "name": {"first": "Decline", "last": "Test"}}, status(201)),
    req("Card with bad Luhn → InvalidAccountNumber", "POST", "/api/nsk/v6/booking/payments", card("4111111111111112"),
        error("nsk:Payment:InvalidAccountNumber")),
    req("Amount above balance → InvalidAmount", "POST", "/api/nsk/v6/booking/payments",
        card().replace("{{balanceDue}}", "99999"), error("nsk:Payment:InvalidAmount")),
    req("Cash as anonymous → InvalidPaymentMethod", "POST", "/api/nsk/v6/booking/payments",
        '{\n  "paymentMethodCode": "CA",\n  "amount": {{balanceDue}}\n}', error("nsk:Payment:InvalidPaymentMethod")),
    req("Declining test card (accepted now, declined at commit)", "POST", "/api/nsk/v6/booking/payments", card("4000000000000002"), T_PAYMENT),
    req("Commit → Payment:Declined (if the flight is within 72h) or Hold", "POST", "/api/nsk/v3/booking", {},
        '''// If the flight is less than 72 hours away the booking cannot be held → 400 nsk:Payment:Declined.
// Otherwise the commit succeeds with status 1 (Hold) and balance still due.
pm.test("declined or held", () => pm.expect([201, 400]).to.include(pm.response.code));
if (pm.response.code === 400) pm.expect(pm.response.json().errors[0].code).to.eql("nsk:Payment:Declined");
'''),
    req("Booking shows the payment as declined (status 4)", "GET", "/api/nsk/v1/booking/payments",
        tests=T_OK + 'pm.test("declined", () => pm.expect(pm.response.json().data.map(p => p.status)).to.include(4));\n'),
    req("Reset booking (releases seats)", "DELETE", "/api/nsk/v1/booking/reset", tests=T_OK),
    req("Logout", "DELETE", "/api/auth/v1/token", tests=T_LOGOUT),
], "Each request checks that the API returns the documented Navitaire-style error code.")

# Pre-request: default travel dates (+14 / +21 days) when not set.
PRE = '''// Default dates: travelDate = today + 14 days, returnDate = today + 21 days (flights are seeded for 30 days).
const addDays = n => new Date(Date.now() + n * 86400000).toISOString().slice(0, 10);
if (!pm.collectionVariables.get("travelDate")) pm.collectionVariables.set("travelDate", addDays(14));
if (!pm.collectionVariables.get("returnDate")) pm.collectionVariables.set("returnDate", addDays(21));'''

variables = [("baseUrl", "http://localhost:5000"), ("origin", "CMB"), ("destination", "DXB"),
             ("travelDate", ""), ("returnDate", ""), ("firstName", "Ashan"), ("lastName", "Dias"),
             ("email", "ashan@example.com"),
             ("token", ""), ("agentToken", ""), ("customerToken", ""), ("legacyToken", ""), ("stationsEtag", ""),
             ("journeyKey", ""), ("fareAvailabilityKey", ""), ("outjourneyKey", ""), ("outfareAvailabilityKey", ""),
             ("retjourneyKey", ""), ("retfareAvailabilityKey", ""), ("cnxjourneyKey", ""), ("cnxfareAvailabilityKey", ""),
             ("passengerKey", ""), ("passengerKey1", ""), ("passengerKey2", ""), ("passengerKey3", ""),
             ("balanceDue", "0"), ("paymentKey", ""), ("recordLocator", ""), ("bookingKey", "")]

collection = {
    "info": {
        "_postman_id": uid("collection"),
        "name": "Pss Engine (Navitaire-style) — Phase 1",
        "description": ("Requests for the Pss airline booking engine through the gateway.\n\n"
                        "1. Import this collection and the 'Pss Local' environment; select the environment.\n"
                        "2. Start the stack (docker compose up --build).\n"
                        "3. Run folder '04 Booking flow' top to bottom (or with the Collection Runner).\n\n"
                        "Test scripts store token, journeyKey, fareAvailabilityKey, passengerKey, balanceDue, recordLocator, bookingKey "
                        "in collection variables, so nothing has to be copied by hand. travelDate/returnDate default to today+14/+21.\n\n"
                        "Full documentation: docs/API_REFERENCE.md"),
        "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
    },
    "auth": {"type": "bearer", "bearer": [{"key": "token", "value": "{{token}}", "type": "string"}]},
    "event": [{"listen": "prerequest", "script": {"type": "text/javascript", "exec": PRE.split("\n")}}],
    "variable": [{"key": k, "value": v, "type": "string"} for k, v in variables],
    "item": [f_health, f_auth, f_ref, f_avail, f_flow, f_family, f_trip, f_manage, f_agent, f_neg],
}

env = {
    "id": uid("env-local"),
    "name": "Pss Local",
    "values": [{"key": "baseUrl", "value": "http://localhost:5000", "type": "default", "enabled": True}],
    "_postman_variable_scope": "environment"
}

with open(os.path.join(OUT, "Pss.postman_collection.json"), "w", encoding="utf-8") as f: json.dump(collection, f, indent=2, ensure_ascii=False)
with open(os.path.join(OUT, "Pss-Local.postman_environment.json"), "w", encoding="utf-8") as f: json.dump(env, f, indent=2)
n = sum(len(x["item"]) for x in collection["item"])
print("folders", len(collection["item"]), "requests", n)

using System.Diagnostics;
using System.Globalization;
using System.Threading.RateLimiting;
using Pss.BuildingBlocks.Observability;

// API Gateway (blueprint §7.16, §8.6): path-based routing to services, CORS, rate limiting on token endpoints,
// correlation id + Server-Timing. Token validation happens in each service (session must exist in Redis).
var builder = WebApplication.CreateBuilder(args);
builder.AddPssObservability("gateway");

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddCors(o => o.AddPolicy("pss-cors", p => p
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("Location", "ETag", "Server-Timing", "X-Correlation-Id")));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("token", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:TokenPerMinute", 120), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("retrieve", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimits:RetrievePerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddHealthChecks();

var app = builder.Build();

app.Use(async (context, next) =>
{
    const string header = "X-Correlation-Id";
    var id = context.Request.Headers[header].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(id)) context.Request.Headers[header] = id = Guid.NewGuid().ToString("N");
    var sw = Stopwatch.StartNew();
    context.Response.OnStarting(() =>
    {
        context.Response.Headers[header] = id;
        var existing = context.Response.Headers["Server-Timing"].ToString();
        var gw = $"gateway;dur={sw.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}";
        context.Response.Headers["Server-Timing"] = string.IsNullOrEmpty(existing) ? gw : existing + ", " + gw;
        return Task.CompletedTask;
    });
    await next();
});

app.UseCors("pss-cors");
app.UseRateLimiter();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Text("Pss Gateway — see README.md for routes"));
app.MapReverseProxy();
app.Run();

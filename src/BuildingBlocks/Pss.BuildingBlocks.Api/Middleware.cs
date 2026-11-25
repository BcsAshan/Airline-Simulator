using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pss.BuildingBlocks.Domain;

namespace Pss.BuildingBlocks.Api;

/// <summary>Converts every exception to the Navitaire error envelope (blueprint §8.3/§8.4/§21).</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, error) = Map(ex);
            if (status >= 500) logger.LogError(ex, "Unhandled exception {Code}", error.Code);
            else logger.LogInformation("Request failed {Status} {Code}: {Message}", status, error.Code, error.Message);

            if (env.IsDevelopment()) error.RawMessage = status >= 500 ? ex.ToString() : ex.Message;

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.Body,
                new ApiResponse<object?> { Data = null, Errors = new List<ApiError> { error } }, PssJson.Options, context.RequestAborted);
        }
    }

    private static (int Status, ApiError Error) Map(Exception ex) => ex switch
    {
        DomainException de => (de.HttpStatus, new ApiError { Code = de.Code, Message = de.Message, Type = de.ErrorType, Details = de.Details }),
        BadHttpRequestException { InnerException: JsonException je } bre => (bre.StatusCode,
            new ApiError { Code = ErrorCodes.MalformedRequest, Message = je.Message, Type = ErrorTypes.Validation }),
        BadHttpRequestException bre when bre.Message.Contains("body", StringComparison.OrdinalIgnoreCase) => (bre.StatusCode,
            new ApiError { Code = ErrorCodes.NullRequest, Message = "The request body is missing or invalid.", Type = ErrorTypes.Validation }),
        BadHttpRequestException bre => (bre.StatusCode,
            new ApiError { Code = ErrorCodes.MalformedRequest, Message = bre.Message, Type = ErrorTypes.Validation }),
        JsonException je => (400, new ApiError { Code = ErrorCodes.MalformedRequest, Message = je.Message, Type = ErrorTypes.Validation }),
        HttpRequestException => (503, new ApiError { Code = ErrorCodes.ServiceUnavailable, Message = "A downstream service is unavailable.", Type = ErrorTypes.Error }),
        TaskCanceledException => (504, new ApiError { Code = ErrorCodes.ServiceUnavailable, Message = "A downstream service timed out.", Type = ErrorTypes.Error }),
        _ => (500, new ApiError { Code = ErrorCodes.UnhandledException, Message = "An unexpected error occurred.", Type = ErrorTypes.Error })
    };
}

/// <summary>Propagates/creates X-Correlation-Id (blueprint §8.2) and puts it in the logging scope.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) id = Guid.NewGuid().ToString("N");
        context.Request.Headers[HeaderName] = id;
        context.TraceIdentifier = id;
        Activity.Current?.SetTag("correlation.id", id);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }
}

/// <summary>Collects Server-Timing entries (session, app, cache …) for the current request.</summary>
public sealed class ServerTimingCollector
{
    private readonly List<string> _entries = new();
    public IReadOnlyList<string> Entries => _entries;

    public void Add(string name, TimeSpan duration, string? description = null)
    {
        var dur = duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);
        _entries.Add(description is null ? $"{name};dur={dur}" : $"{name};desc=\"{description}\";dur={dur}");
    }

    public async Task<T> Measure<T>(string name, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        try { return await action(); }
        finally { Add(name, sw.Elapsed); }
    }
}

/// <summary>Emits "Server-Timing: api;dur=..,session;dur=..,app;dur=.." like dotREZ.</summary>
public sealed class ServerTimingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        context.Response.OnStarting(() =>
        {
            var parts = new List<string> { $"api;dur={sw.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)}" };
            var collector = context.RequestServices.GetService<ServerTimingCollector>();
            if (collector is not null) parts.AddRange(collector.Entries);
            context.Response.Headers["Server-Timing"] = string.Join(", ", parts);
            return Task.CompletedTask;
        });
        await next(context);
    }
}

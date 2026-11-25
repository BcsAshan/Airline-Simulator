using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Pss.BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Serilog (console + optional Seq) and OpenTelemetry tracing (ASP.NET Core, HttpClient, MassTransit → OTLP).
    /// Never log payment fields, passwords or tokens (blueprint §19) — services must not log request bodies.
    /// </summary>
    public static WebApplicationBuilder AddPssObservability(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((context, _, logger) =>
        {
            // Quiet defaults. Serilog does NOT read the "Logging" section of appsettings, so without these overrides every
            // EF Core SQL command (including the MassTransit outbox poll that runs every second) is printed at Information.
            // A "Serilog" section in appsettings / env vars (e.g. Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Information)
            // still overrides these because ReadFrom.Configuration runs afterwards.
            logger.MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("MassTransit", LogEventLevel.Warning)
                .MinimumLevel.Override("Yarp", LogEventLevel.Warning);

            logger.ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {Message:lj} {Properties:j}{NewLine}{Exception}");

            var seqUrl = context.Configuration["Seq:Url"];
            if (!string.IsNullOrWhiteSpace(seqUrl)) logger.WriteTo.Seq(seqUrl);
        });

        var otlp = builder.Configuration["Otel:Endpoint"];
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                 .AddHttpClientInstrumentation()
                 .AddSource("MassTransit");
                if (!string.IsNullOrWhiteSpace(otlp)) t.AddOtlpExporter(o => o.Endpoint = new Uri(otlp));
            });

        return builder;
    }
}

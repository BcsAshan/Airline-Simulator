using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Pss.BuildingBlocks.Api;

public static class ServiceDefaults
{
    /// <summary>JSON, envelope-friendly model binding, Server-Timing, Swagger and health checks.</summary>
    public static WebApplicationBuilder AddPssApi(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Services.ConfigureHttpJsonOptions(o => PssJson.Configure(o.SerializerOptions));
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ServerTimingCollector>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = serviceName, Version = "v1" });
            c.CustomSchemaIds(t => t.FullName?.Replace('+', '.'));
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Name = "Authorization"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });
        });
        builder.Services.AddHealthChecks();
        return builder;
    }

    /// <summary>Correlation id → Server-Timing → error envelope. Call before authentication and endpoints.</summary>
    public static WebApplication UsePssApi(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ServerTimingMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }

    /// <summary>/health (all), /health/live (process only), /health/ready (dependencies) — blueprint §24.</summary>
    public static WebApplication MapPssHealth(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");
        return app;
    }
}

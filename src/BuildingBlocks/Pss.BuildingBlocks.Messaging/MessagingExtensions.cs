using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pss.BuildingBlocks.Messaging;

/// <summary>
/// RabbitMQ + MassTransit wiring (blueprint §6.2, §17). Each service gets its own queue names via a prefix,
/// so several services can consume the same event type independently.
/// </summary>
public static class MessagingExtensions
{
    /// <summary>Bus without transactional outbox (services that do not own a SQL database).</summary>
    public static IServiceCollection AddPssMessaging(this IServiceCollection services, IConfiguration configuration,
        string serviceName, Action<IBusRegistrationConfigurator>? configure = null)
        => AddCore(services, configuration, serviceName, configure);

    /// <summary>
    /// Bus with the EF Core transactional outbox + inbox (blueprint §6.3: every event is written in the same
    /// transaction as the state change; consumers are idempotent). Publish through the scoped IPublishEndpoint
    /// and call SaveChanges on <typeparamref name="TDbContext"/>.
    /// </summary>
    public static IServiceCollection AddPssMessaging<TDbContext>(this IServiceCollection services, IConfiguration configuration,
        string serviceName, Action<IBusRegistrationConfigurator>? configure = null)
        where TDbContext : DbContext
        => AddCore(services, configuration, serviceName, x =>
        {
            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UseSqlServer();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
            });
            x.AddConfigureEndpointsCallback((context, _, cfg) => cfg.UseEntityFrameworkOutbox<TDbContext>(context));
            configure?.Invoke(x);
        });

    private static IServiceCollection AddCore(IServiceCollection services, IConfiguration configuration,
        string serviceName, Action<IBusRegistrationConfigurator>? configure)
    {
        services.AddMassTransit(x =>
        {
            x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, false));
            configure?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(configuration["RabbitMq:Host"] ?? "localhost", configuration["RabbitMq:VirtualHost"] ?? "/", h =>
                {
                    h.Username(configuration["RabbitMq:Username"] ?? "guest");
                    h.Password(configuration["RabbitMq:Password"] ?? "guest");
                });
                cfg.UseMessageRetry(r => r.Intervals(100, 500, 1000, 5000));
                cfg.ConfigureEndpoints(context);
            });
        });
        return services;
    }

    /// <summary>Adds MassTransit inbox/outbox tables to a service DbContext model.</summary>
    public static void AddPssOutboxEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

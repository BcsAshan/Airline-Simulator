using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pss.BuildingBlocks.Persistence;

public static class PersistenceExtensions
{
    /// <summary>Registers a SQL Server DbContext (one database per service, blueprint §18) + readiness check.</summary>
    public static IServiceCollection AddPssSqlServer<TDbContext>(this IServiceCollection services, IConfiguration configuration)
        where TDbContext : DbContext
    {
        var cs = configuration.GetConnectionString("Sql")
                 ?? throw new InvalidOperationException("ConnectionStrings:Sql is not configured.");
        services.AddDbContext<TDbContext>(o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(5)));
        services.AddHealthChecks().AddDbContextCheck<TDbContext>("sql", tags: new[] { "ready" });
        return services;
    }

    /// <summary>
    /// Phase 0/1: creates the database from the model (EnsureCreated) and runs the seeder, retrying while
    /// SQL Server starts. TODO(Phase 2): replace with EF migrations bundles (blueprint §24).
    /// </summary>
    public static async Task InitializeDatabaseAsync<TDbContext>(this WebApplication app,
        Func<TDbContext, IServiceProvider, Task>? seed = null)
        where TDbContext : DbContext
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                await db.Database.EnsureCreatedAsync();
                if (seed is not null) await seed(db, scope.ServiceProvider);
                logger.LogInformation("Database for {Context} is ready", typeof(TDbContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < 30)
            {
                logger.LogWarning("Database not ready ({Attempt}/30): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>Money precision convention: decimal(18,4) (blueprint §18).</summary>
    public static void ApplyPssConventions(this ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 4);
    }
}

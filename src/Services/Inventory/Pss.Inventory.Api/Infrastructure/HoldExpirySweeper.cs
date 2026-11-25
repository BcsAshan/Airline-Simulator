using Pss.Inventory.Api.Application;

namespace Pss.Inventory.Api.Infrastructure;

public sealed class HoldExpirySweeper(IServiceScopeFactory scopes, ILogger<HoldExpirySweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var released = await scope.ServiceProvider.GetRequiredService<InventoryService>().SweepExpiredAsync(stoppingToken);
                if (released > 0) logger.LogInformation("Expired {Count} stale holds", released);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Hold sweep failed");
            }
        }
    }
}

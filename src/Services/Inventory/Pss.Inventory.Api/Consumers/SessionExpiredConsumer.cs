using MassTransit;
using Pss.Contracts.Events;
using Pss.Inventory.Api.Application;

namespace Pss.Inventory.Api.Consumers;

/// <summary>Session expired / logged out → release its uncommitted holds (blueprint §5, §17).</summary>
public sealed class ReleaseHoldsOnSessionExpiredConsumer(InventoryService inventory) : IConsumer<SessionExpired>
{
    public Task Consume(ConsumeContext<SessionExpired> context)
        => inventory.ReleaseSessionAsync(context.Message.SessionId, context.CancellationToken);
}

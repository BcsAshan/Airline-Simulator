using MassTransit;
using Pss.Booking.Api.Infrastructure;
using Pss.Contracts.Events;

namespace Pss.Booking.Api.Consumers;

/// <summary>Session expired / logged out → drop the booking in state (Inventory releases the holds).</summary>
public sealed class DropDraftOnSessionExpiredConsumer(DraftStore drafts) : IConsumer<SessionExpired>
{
    public Task Consume(ConsumeContext<SessionExpired> context) => drafts.DeleteAsync(context.Message.SessionId);
}

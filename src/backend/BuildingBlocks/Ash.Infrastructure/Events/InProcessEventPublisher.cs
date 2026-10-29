using Ash.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Infrastructure.Events;

/// <summary>Runs handlers in-process, in the caller's scope (same unit of work / transaction).</summary>
public sealed class InProcessEventPublisher(IServiceProvider services) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }
}

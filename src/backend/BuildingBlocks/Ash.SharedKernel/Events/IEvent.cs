namespace Ash.SharedKernel.Events;

/// <summary>An in-process domain event. Event types live in the publishing module's Contracts.</summary>
public interface IEvent
{
}

public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent;
}

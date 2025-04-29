using NLog;

using System.Collections.Concurrent;

namespace RockEngine.Core.EventSystem
{
    public class EventBus : IEventBus
    {
        private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
        private readonly ILogger _logger;

        public EventBus(ILogger logger)
        {
            _logger = logger;
        }

        public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                foreach (var handler in handlers.OfType<IEventHandler<TEvent>>())
                {
                    Task.Run(() => handler.Handle(@event))
                        .ContinueWith(t =>
                            _logger.Error($"Error handling event {typeof(TEvent).Name}: {t.Exception}"),
                            TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }

        public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            _handlers.AddOrUpdate(eventType,
                [handler],
                (_, existing) => { existing.Add(handler); return existing; });
        }
    }
}

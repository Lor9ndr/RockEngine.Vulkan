using System.Collections.Concurrent;

namespace RockEngine.Vulkan.EventSystem
{
    public interface IEventSystem
    {
        void Register<TEvent>(Func<TEvent, Task> handler) where TEvent : EventBase;
        void Register<TEvent>(Action<TEvent> handler) where TEvent : EventBase;
        void Unregister<TEvent>(Func<TEvent, Task> handler) where TEvent : EventBase;
        void Unregister<TEvent>(Action<TEvent> handler) where TEvent : EventBase;
        Task RaiseAsync<TEvent>(TEvent eventArgs) where TEvent : EventBase;
    }

    public class EventSystem : IEventSystem
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

        public void Register<TEvent>(Func<TEvent, Task> handler) where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            if (!_handlers.ContainsKey(eventType))
            {
                _handlers[eventType] = new List<Delegate>();
            }
            _handlers[eventType].Add(handler);
        }

        public void Register<TEvent>(Action<TEvent> handler) where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            if (!_handlers.ContainsKey(eventType))
            {
                _handlers[eventType] = new List<Delegate>();
            }
            _handlers[eventType].Add(handler);
        }

        public void Unregister<TEvent>(Func<TEvent, Task> handler) where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out List<Delegate>? value))
            {
                value.Remove(handler);
            }
        }

        public void Unregister<TEvent>(Action<TEvent> handler) where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out List<Delegate>? value))
            {
                value.Remove(handler);
            }
        }

        public async Task RaiseAsync<TEvent>(TEvent eventArgs) where TEvent : EventBase
        {
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out List<Delegate>? value))
            {
                var tasks = value.OfType<Func<TEvent, Task>>()
                    .Select(handler => handler(eventArgs));

                var actions = value.OfType<Action<TEvent>>()
                    .Select(handler => Task.Run(() => handler(eventArgs)));

                await Task.WhenAll(tasks.Concat(actions));
            }
        }
    }
}

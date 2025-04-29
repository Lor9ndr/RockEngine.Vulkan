namespace RockEngine.Core.EventSystem
{
    public interface IEventBus
    {
        void Publish<TEvent>(TEvent @event) where TEvent : IEvent;
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
    }
}

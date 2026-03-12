namespace RockEngine.Core.Helpers
{
    public class WeakEvent<TEventArgs> where TEventArgs : EventArgs
    {
        private readonly List<WeakReference<EventHandler<TEventArgs>>> _eventHandlers = new();

        public void AddHandler(EventHandler<TEventArgs> handler)
        {
            if (handler != null)
            {
                _eventHandlers.Add(new WeakReference<EventHandler<TEventArgs>>(handler));
            }
        }

        public void Raise(object sender, TEventArgs args)
        {
            // Iterate backwards to safely remove dead references
            for (int i = _eventHandlers.Count - 1; i >= 0; i--)
            {
                var weakRef = _eventHandlers[i];
                if (weakRef.TryGetTarget(out var handler))
                {
                    handler.Invoke(sender, args);
                }
                else
                {
                    _eventHandlers.RemoveAt(i); // Remove dead handler
                }
            }
        }
        public void RemoveHandler(EventHandler<TEventArgs> handler)
        {
            _eventHandlers.RemoveAll(s=> !s.TryGetTarget(out var target) || target == handler);
        }
    }

}

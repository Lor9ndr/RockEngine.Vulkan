namespace RockEngine.Vulkan
{
    /// <summary>
    /// Реализация механизма отслеживания изменений ресурса с автоматической очисткой мёртвых наблюдателей.
    /// Используйте композицию в любом классе, который должен поддерживать IResourceTrackable.
    /// </summary>
    public sealed class ResourceTracker : IResourceTrackable
    {
        private ulong _nextObserverId = 0;
        // Возможно стоит использовать WeakReference. но пока утечек памяти не нашел. 
        private readonly Dictionary<ulong, IResourceObserver> _observers = new();
        private readonly Lock _observerLock = new();
        private readonly ulong _id;

        public ulong ID => _id;

        public ResourceTracker()
        {
            _id = ResourceIdentifier.GetID(); // ваш генератор ID
        }

        /// <summary>
        /// Подписать наблюдателя.
        /// </summary>
        /// <returns>Токен для отписки (Dispose).</returns>
        public IDisposable Subscribe(IResourceObserver observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            ulong subscriptionId;
            lock (_observerLock)
            {
                subscriptionId = _nextObserverId++;
                _observers[subscriptionId] = observer;
            }
            return new SubscriptionToken(this, subscriptionId);
        }

        /// <summary>
        /// Уведомить всех живых наблюдателей об изменении.
        /// </summary>
        public void NotifyObservers(ResourceChangeType changeType)
        {
            lock (_observerLock)
            {
                foreach (var kvp in _observers)
                {
                    kvp.Value.OnResourceChanged(_id, changeType);
                }
            }
        }

        /// <summary>
        /// Очистить всех наблюдателей (например, при уничтожении ресурса).
        /// </summary>
        public void Clear()
        {
            lock (_observerLock)
            {
                _observers.Clear();
            }
        }

        /// <summary>
        /// Есть ли активные подписчики (для оптимизации).
        /// </summary>
        public bool HasObservers
        {
            get
            {
                lock (_observerLock)
                {
                    return _observers.Count > 0;
                }
            }
        }

        private sealed class SubscriptionToken : IDisposable
        {
            private readonly ResourceTracker _owner;
            private readonly ulong _subscriptionId;
            private bool _disposed;

            public SubscriptionToken(ResourceTracker owner, ulong id)
            {
                _owner = owner;
                _subscriptionId = id;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                lock (_owner._observerLock)
                {
                    _owner._observers.Remove(_subscriptionId);
                }
                _disposed = true;
            }
        }
    }
}
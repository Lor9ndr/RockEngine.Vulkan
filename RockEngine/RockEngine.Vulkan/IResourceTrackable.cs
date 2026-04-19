namespace RockEngine.Vulkan
{

    public static class ResourceIdentifier
    {
        private static uint _globalResourceCounter;

        public static ulong GetID() => Interlocked.Increment(ref _globalResourceCounter);
    }
    public interface IResourceTrackable
    {
        /// <summary>
        /// Подписаться на уведомления об изменении ресурса.
        /// </summary>
        /// <returns>Токен для отписки.</returns>
        IDisposable Subscribe(IResourceObserver observer);
        public ulong ID { get; }
    }

    public interface IResourceObserver
    {
        void OnResourceChanged(ulong resourceId, ResourceChangeType changeType);
    }

    public enum ResourceChangeType
    {
        DataUpdated,    // содержимое изменилось (например, новый мип-уровень)
        Resized,        // изменился размер
        Disposed        // ресурс уничтожен
    }
}


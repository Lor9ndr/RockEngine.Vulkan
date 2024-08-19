namespace RockEngine.Vulkan.Helpers.Abstractions.Cache
{
    public interface ICache<TKey, TValue>
    {
        bool TryGet(TKey key, out TValue value);
        void Set(TKey key, TValue value);
    }
}

namespace RockEngine.Core.Registries
{
    public interface IRegistry<TValue, TKey> : IDisposable
    {
        public TValue? Get(TKey key);
        public void Register(TKey key, TValue value);
        public void Unregister(TKey key);
    }
}

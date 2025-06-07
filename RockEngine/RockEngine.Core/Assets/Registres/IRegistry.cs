namespace RockEngine.Core.Assets.Registres
{
    public interface IRegistry<T>
    {
        void Register<TKey>(TKey key, T value);
        T Get<TKey>(TKey key);
        bool TryGet<TKey>(TKey key, out T value);
    }

}

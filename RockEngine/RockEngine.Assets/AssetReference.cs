namespace RockEngine.Assets
{
    public interface IAssetReference<T> where T : class, IAsset
    {
        public Guid AssetID { get; }
        public T Get();
        Task<T> GetAssetAsync();
        bool IsResolved { get; }
    }

    
}
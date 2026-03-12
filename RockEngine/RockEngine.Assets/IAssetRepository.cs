namespace RockEngine.Assets
{
    public interface IAssetRepository
    {
        void Add(IAsset asset);
        bool TryGet(Guid id, out IAsset asset);
        bool TryGet(string path, out IAsset asset);
        void Remove(Guid id);
        void Remove(string path);
        IEnumerable<IAsset> GetAll();
        void Clear();
    }
}

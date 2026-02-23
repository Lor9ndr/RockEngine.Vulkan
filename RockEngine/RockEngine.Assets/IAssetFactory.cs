namespace RockEngine.Assets
{
    public interface IAssetFactory
    {
        T Create<T>(AssetPath path, string? name = null) where T : IAsset;
        IAsset Create(AssetPath path, Type type, string? name = null);
        
        Task<IAsset> CreateModelFromFileAsync(string filePath, string? modelName = null, string parentPath = "Models");
        IAsset CreateMaterial(string name, string template, List<IAssetReference<IAsset>>? textures = null, Dictionary<string, object>? parameters = null);
    }
}
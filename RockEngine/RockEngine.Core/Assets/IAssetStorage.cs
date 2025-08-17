
namespace RockEngine.Core.Assets
{
    public interface IAssetStorage
    {
        Task SaveAsync<T>(IAsset<T> asset) where T : class;
        Task<IAsset<TData>> LoadAsync<TAsset, TData>(AssetPath path) where TAsset : IAsset<TData> where TData : class;
        Task<IAsset<T>> LoadAsync<T>(AssetPath path) where T : class;
        Task<ProjectAsset> CreateProjectAsync(string projectName, string basePath);
        Task<ProjectAsset> LoadProjectAsync(string projectName, string basePath);
        Task SaveProjectAsync(ProjectAsset project);

        bool Exists(AssetPath path);
        T? GetAsset<T>(Guid assetID) where T : class, IAsset;
    }
}
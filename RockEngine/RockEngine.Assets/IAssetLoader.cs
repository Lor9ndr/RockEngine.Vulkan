using System;
using System.Collections.Generic;
using System.Text;

namespace RockEngine.Assets
{
    public interface IAssetLoader
    {
        Task<IAsset> LoadAssetAsync(Guid assetId);
        Task<IAsset> LoadAssetAsync(string assetPath);
        Task LoadAssetDataAsync<T>(IAsset<T> asset) where T : class;
        Task LoadAssetDataAsync(IAsset asset, Type dataType);
        Task<T> LoadAssetAsync<T>(Guid assetId) where T : class, IAsset;
        Task<T> LoadAssetAsync<T>(string assetPath) where T : class, IAsset;
        void SetBasePath(string basePath);
    }
   
}

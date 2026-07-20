
using MemoryPack;
using RockEngine.Assets;
using RockEngine.Core.DI;

namespace RockEngine.Core.Assets
{
    public abstract class Asset<T> : IAsset<T> where T : class, new()
    {
        public T? Data { get; protected set; }
        private readonly HashSet<IAsset> _dependencies = new HashSet<IAsset>();
        protected readonly SemaphoreSlim _fileSemaphore = new(1, 1);

        protected Asset()
        {
            Data = null;
        }

        public Guid ID { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public virtual string Type => typeof(T).Name;
        public AssetPath Path { get; set; } = AssetPath.Empty;
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime Modified { get; set; } = DateTime.UtcNow;

        [MemoryPackIgnore]
        public bool IsDataLoaded => Data != null;
        public HashSet<IAsset> Dependencies => _dependencies;

        public void UpdateModified() => Modified = DateTime.UtcNow;

        public virtual async Task LoadDataAsync()
        {
            if (IsDataLoaded)
            {
                return;
            }

            await _fileSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!IsDataLoaded)
                {
                    var assetManager = IoC.Container.GetInstance<IAssetManager>();
                    await assetManager.LoadAssetDataAsync(this).ConfigureAwait(false);
                }
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }

        public virtual void BeforeSaving() { }
        public virtual void AfterSaving() { }

        public virtual void UnloadData()
        {
            Data = null;
        }

        // Non-generic implementation
        public virtual void SetData(object data)
        {
            if (data is T typedData)
            {
                Data = typedData;
            }
            else
            {
                throw new ArgumentException($"Expected data of type {typeof(T)}, got {data?.GetType()}");
            }
        }

        public Type GetDataType() => typeof(T);
        public object GetData() => Data;

    }
}

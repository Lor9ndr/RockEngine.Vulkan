using RockEngine.Core.DI;

namespace RockEngine.Core.Assets
{
    public interface IAsset
    {
        Guid ID { get; }
        string Name { get; set; }
        string Type { get; }
        DateTime Created { get; }
        DateTime Modified { get; }
        AssetPath Path { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        bool IsDataLoaded { get; }

        IEnumerable<IAsset> Dependencies { get; }
        void AddDependency(IAsset asset);
        void UpdateModified();
        Task LoadDataAsync();
        void UnloadData();
        Type GetDataType();
        object GetData();
        void SetData(object data);

        void BeforeSaving();
        void AfterSaving();
    }

    public interface IAsset<TData> : IAsset where TData : class
    {
        [System.Text.Json.Serialization.JsonIgnore]
        TData? Data { get; }
    }

    public abstract class Asset<T> : IAsset<T> where T : class, new()
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public T? Data { get; protected set; }

        private readonly HashSet<IAsset> _dependencies = new HashSet<IAsset>();
        internal readonly SemaphoreSlim _fileSemaphore = new(1, 1);

        [System.Text.Json.Serialization.JsonConstructor]
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

        public bool IsDataLoaded => Data != null;
        public IEnumerable<IAsset> Dependencies => _dependencies.ToList().AsReadOnly();

        public void AddDependency(IAsset asset)
        {
            if (asset == null || asset.ID == ID) return;
            if (!_dependencies.Any(d => d.ID == asset.ID))
            {
                _dependencies.Add(asset);
            }
        }

        public void UpdateModified() => Modified = DateTime.UtcNow;

        public virtual async Task LoadDataAsync()
        {
            if (IsDataLoaded) return;

            await _fileSemaphore.WaitAsync();
            try
            {
                if (!IsDataLoaded)
                {
                    var assetManager = IoC.Container.GetInstance<AssetManager>();
                    await assetManager.LoadAssetDataAsync(this);
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
                Data = typedData;
            else
                throw new ArgumentException($"Expected data of type {typeof(T)}, got {data?.GetType()}");
        }

        public Type GetDataType() => typeof(T);
        public object GetData() => Data;
    }
}
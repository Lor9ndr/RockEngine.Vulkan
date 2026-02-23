using MessagePack;

using RockEngine.Assets;
using RockEngine.Core.DI;

namespace RockEngine.Core.Assets
{
    [Union(0,typeof(MeshAsset))]
    [Union(1,typeof(SceneAsset))]
    [Union(2,typeof(ModelAsset))]
    [Union(3,typeof(MaterialAsset))]
    [Union(4,typeof(TextureAsset))]
    public abstract class Asset<T> : IAsset<T> where T : class, new()
    {
        [IgnoreMember]
        public T? Data { get; protected set; }
        [Key(-1)]
        private readonly HashSet<IAsset> _dependencies = new HashSet<IAsset>();
        [IgnoreMember]
        protected readonly SemaphoreSlim _fileSemaphore = new(1, 1);

        protected Asset()
        {
            Data = null;
        }

        [Key(0)]
        public Guid ID { get; set; } = Guid.NewGuid();
        [Key(1)]
        public string Name { get; set; } = string.Empty;
        [Key(2)]
        public virtual string Type => typeof(T).Name;
        [Key(3)]
        public AssetPath Path { get; set; } = AssetPath.Empty;
        [Key(4)]
        public DateTime Created { get; set; } = DateTime.UtcNow;
        [Key(5)]
        public DateTime Modified { get; set; } = DateTime.UtcNow;

        [Key(14)]
        public bool IsDataLoaded => Data != null;
        [Key(6)]
        public HashSet<IAsset> Dependencies => _dependencies;

        public void UpdateModified() => Modified = DateTime.UtcNow;

        public virtual async Task LoadDataAsync()
        {
            if (IsDataLoaded) return;

            await _fileSemaphore.WaitAsync();
            try
            {
                if (!IsDataLoaded)
                {
                    var assetManager = IoC.Container.GetInstance<IAssetManager>();
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

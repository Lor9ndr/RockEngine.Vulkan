using Newtonsoft.Json;

using RockEngine.Core.DI;

using System.Reflection;

namespace RockEngine.Core.Assets
{
    public interface IAsset
    {
        Guid ID { get; }
        string Name { get; set; }
        string Type { get; }
        AssetPath Path { get; set; }
        DateTime Created { get; }
        DateTime Modified { get; }
        bool IsDataLoaded { get; }
        IEnumerable<IAsset> Dependencies { get; }
        void AddDependency(IAsset asset);
        void UpdateModified();
        Task LoadDataAsync();
        void UnloadData();
        Type GetDataType();
        object GetData();
        void SetData(object data);
    }

    public interface IAsset<TData> : IAsset where TData : class
    {
        [JsonIgnore] // Data comes later
        TData? Data { get;}
    }

    public class Asset<T> :  IAsset<T> where T : class, new()
    {
        public T? Data { get; protected set;} = new T();

        private readonly HashSet<IAsset> _dependencies = new HashSet<IAsset>();
        protected readonly SemaphoreSlim _loadSemaphore = new(1, 1);


       
        [JsonConstructor]
        protected Asset() { }

        [JsonProperty(Order = -20)] // Ensure this comes first in serialization
        public Guid ID { get; set; } = Guid.NewGuid();

        [JsonProperty(Order = -19)]
        public string Name { get; set; } = string.Empty;

        [JsonProperty(Order = -18)]
        public virtual string Type { get; } = "EMPTY ASSET";

        [JsonProperty(Order = -17)]
        public AssetPath Path { get; set; } = AssetPath.Empty;

        [JsonProperty(Order = -16)]
        public DateTime Created { get; set; } = DateTime.UtcNow;

        [JsonProperty(Order = -15)]
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
            if (!IsDataLoaded)
            {
                await _loadSemaphore.WaitAsync();
                try
                {
                    if (!IsDataLoaded)
                    {
                        var assetManager = IoC.Container.GetInstance<AssetManager>();
                        await assetManager.LoadFullAsync<T>(this.Path);
                   }
                }
                finally
                {
                    _loadSemaphore.Release();
                }
            }
        }

        public virtual void UnloadData()
        {
            Data = null;
        }

        public virtual void SetData(object data) => Data = (T)data;
        public Type GetDataType() => typeof(T);

        public object GetData() => Data;
    }

   
}

using Newtonsoft.Json;

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


        [JsonIgnore] 
        bool IsDataLoaded { get; }
        IEnumerable<IAsset> Dependencies { get; }
        void AddDependency(IAsset asset);
        void UpdateModified();
        Task LoadDataAsync();
        void UnloadData();
        Type GetDataType();
        object GetData();
        void SetData(object data);

        /// <summary>
        /// Method is being called before saving data /> 
        /// </summary>
        void BeforeSaving();

        /// <summary>
        /// Method is being called after saving data/> 
        /// </summary>
        void AfterSaving();
    }

    public interface IAsset<TData> : IAsset where TData : class
    {
        [JsonIgnore] // Data comes later
        TData? Data { get;}

       
    }

    public class Asset<T> :  IAsset<T> where T : class, new()
    {
        [JsonIgnore]
        public T? Data { get; protected set;}

        private readonly HashSet<IAsset> _dependencies = new HashSet<IAsset>();
        protected readonly SemaphoreSlim _loadSemaphore = new(1, 1);
       
        [JsonConstructor]
        protected Asset()
        {
            Data = null;
        }

        public Guid ID { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public virtual string Type { get; } = "EMPTY ASSET";
        public AssetPath Path { get; set; } = AssetPath.Empty;


        public DateTime Created { get; set; } = DateTime.UtcNow;

        public DateTime Modified { get; set; } = DateTime.UtcNow;

        public bool IsDataLoaded => Data != null;
        public IEnumerable<IAsset> Dependencies => _dependencies.ToList().AsReadOnly();


        public void AddDependency(IAsset asset)
        {
            if (asset == null || asset.ID == ID)
            {
                return;
            }

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
                        await assetManager.LoadAsync<T>(Path);
                   }
                }
                finally
                {
                    _loadSemaphore.Release();
                }
            }
        }
     


        public virtual void BeforeSaving()
        {
        }

     
        public virtual void AfterSaving()
        {

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

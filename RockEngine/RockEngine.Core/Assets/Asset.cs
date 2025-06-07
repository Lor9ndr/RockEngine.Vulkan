namespace RockEngine.Core.Assets
{
    public abstract class Asset : IAsset
    {
        public Guid ID { get; } = Guid.NewGuid();
        public string Name { get; protected set; }
        public string Path { get; set; }
        public bool IsLoaded { get; protected set; }

        protected Asset(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        public abstract Task LoadAsync();
        public abstract void Unload();

        public virtual void Dispose() => Unload();
    }

}

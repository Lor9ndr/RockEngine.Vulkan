namespace RockEngine.Core.Assets
{
    public interface IAsset : IDisposable
    {
        Guid ID { get; }
        string Name { get; }
        string Path { get; set; }
        bool IsLoaded { get; }
        Task LoadAsync();
        void Unload();
    }
}

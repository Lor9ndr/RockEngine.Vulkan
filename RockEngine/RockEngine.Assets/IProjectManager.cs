namespace RockEngine.Assets
{
    public interface IProjectManager
    {
        IProject? CurrentProject { get; }
        Task<T> CreateProjectAsync<T,TData>(string projectPath, string projectName) where T:class,IProject, IAsset<TData> where TData : class, new();
        Task<T> LoadProjectAsync<T>(string projectFilePath) where T : class, IProject;
        void UnloadProject();
        bool IsProjectLoaded { get; }
    }
}
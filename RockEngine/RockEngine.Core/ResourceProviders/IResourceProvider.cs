namespace RockEngine.Core.ResourceProviders
{

    public interface IResourceProvider
    {
        // Non-generic interface for type-agnostic operations
    }

    public interface IResourceProvider<T> : IResourceProvider
    {
        ValueTask<T> GetAsync();
    }
}

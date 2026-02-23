namespace RockEngine.Core
{
    public interface IApplicationContext
    {
        Task InitializeAsync();

        Task UpdateAsync();

        Task RenderAsync();
    }
}

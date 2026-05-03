using RockEngine.Core.ECS;
using RockEngine.Core.Rendering;

namespace RockEngine.Core
{
    public interface IApplicationContext
    {
        /// <summary>
        /// Called once after window and graphics are ready.
        /// </summary>
        Task InitializeAsync(GraphicsContext graphics, WorldRenderer renderer, World world);

        /// <summary>
        /// Called every frame before rendering.
        /// </summary>
        Task UpdateAsync();

        /// <summary>
        /// Called every frame to issue rendering commands.
        /// </summary>
        Task RenderAsync(RenderContext renderContext);

        /// <summary>
        /// Called when the application is shutting down.
        /// </summary>
        Task ShutdownAsync();
    }
    public abstract class ApplicationContextBase : IApplicationContext
    {
        public virtual Task InitializeAsync(GraphicsContext graphics, WorldRenderer renderer, World world)
            => Task.CompletedTask;

        public virtual Task UpdateAsync() => Task.CompletedTask;
        public virtual Task RenderAsync(RenderContext renderContext) => Task.CompletedTask;
        public virtual Task ShutdownAsync() => Task.CompletedTask;
    }
}

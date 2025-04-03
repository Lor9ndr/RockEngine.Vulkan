using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Systems
{
    internal class CameraSystem : ISystem
    {
        public int Priority => 400;
        private readonly Renderer _renderer;

        public CameraSystem(Renderer renderer) => _renderer = renderer;

        public ValueTask Update(World world, float deltaTime)
        {
            /* foreach (var entity in world.GetEntities())
             {
                 if(entity.TryGetComponent<Camera>(out var camera))
                 {
                     camera.UpdateVectors(entity);
                     _renderer.CurrentCamera = camera;
                     break; // Use first active camera
                 }

             }*/
            return ValueTask.CompletedTask;
        }
    }
}

using RockEngine.Core;
using RockEngine.Core.ECS;
using RockEngine.Core.Physics;

namespace RockEngine.Editor
{
    public class EditorContext : IApplicationContext
    {
        private readonly PhysicsManager _physicsManager;
        private readonly EditorStateManager _stateManager;
        private readonly World _world;

        public EditorContext(PhysicsManager physicsManager, EditorStateManager stateManager, World world)
        {
            _physicsManager = physicsManager;
            _stateManager = stateManager;
            _world = world;
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task RenderAsync()
        {
          
            return Task.CompletedTask;
        }

        public Task UpdateAsync()
        {
            if (_stateManager.State == EditorState.Play)
            {
                _physicsManager.Update(Time.DeltaTime);
            }
            return Task.CompletedTask;
        }
    }
}

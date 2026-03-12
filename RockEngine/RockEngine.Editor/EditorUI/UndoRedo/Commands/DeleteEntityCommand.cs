using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class DeleteEntityCommand : IUndoRedoCommand
    {
        private readonly World _world;
        private readonly Entity _entity;
        // Snapshot data
        private readonly string _name;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Vector3 _scale;
        private readonly List<IComponent> _components; // store component instances (they will be detached)

        public DeleteEntityCommand(World world, Entity entity)
        {
            _world = world;
            _entity = entity;
            // Capture state before deletion
            _name = entity.Name;
            _position = entity.Transform.Position;
            _rotation = entity.Transform.Rotation;
            _scale = entity.Transform.Scale;
            _components = new List<IComponent>(entity.Components);
        }

        public void Execute()
        {
            _world.RemoveEntity(_entity);
        }

        public void Undo()
        {
            // Re‑create the entity with the same ID? Not possible directly.
            // For a robust implementation, you would store a full serialized snapshot
            // (including component data) and recreate a new entity, then update any
            // references. Here we throw to indicate this needs proper implementation.
            throw new NotImplementedException("Undo for DeleteEntityCommand requires a full snapshot system.");
        }
    }
}
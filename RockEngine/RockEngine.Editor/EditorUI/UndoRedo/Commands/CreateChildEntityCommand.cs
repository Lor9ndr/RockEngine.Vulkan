using RockEngine.Core.ECS;

namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class CreateChildEntityCommand : IUndoRedoCommand
    {
        private readonly World _world;
        private readonly Entity _parent;
        private Entity _createdEntity;
        private readonly string? _initialName;

        public CreateChildEntityCommand(World world, Entity parent, string? initialName = null)
        {
            _world = world;
            _parent = parent;
            _initialName = initialName;
        }

        public void Execute()
        {
            _createdEntity = _world.CreateEntity(_initialName);
            _parent.AddChild(_createdEntity);
        }

        public void Undo()
        {
            if (_createdEntity != null)
            {
                _world.RemoveEntity(_createdEntity);
                _createdEntity = null;
            }
        }
    }
}
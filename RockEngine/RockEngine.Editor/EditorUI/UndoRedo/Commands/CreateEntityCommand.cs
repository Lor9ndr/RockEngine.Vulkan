using RockEngine.Core.ECS;



namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class CreateEntityCommand : IUndoRedoCommand
    {
        private readonly World _world;
        private Entity _createdEntity;
        private readonly string _initialName;

        public CreateEntityCommand(World world, string initialName = null)
        {
            _world = world;
            _initialName = initialName;
        }

        public void Execute()
        {
            _createdEntity = _world.CreateEntity(_initialName);
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
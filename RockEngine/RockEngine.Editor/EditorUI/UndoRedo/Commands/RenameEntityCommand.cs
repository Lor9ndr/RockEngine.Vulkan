using RockEngine.Core.ECS;

namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class RenameEntityCommand : IUndoRedoCommand
    {
        private readonly Entity _entity;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameEntityCommand(Entity entity, string oldName, string newName)
        {
            _entity = entity;
            _oldName = oldName;
            _newName = newName;
        }

        public void Execute()
        {
            _entity.Name = _newName;
        }

        public void Undo()
        {
            _entity.Name = _oldName;
        }
    }
}

using RockEngine.Core.ECS;

namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class SetParentCommand : IUndoRedoCommand
    {
        private readonly Entity _entity;
        private readonly Entity _oldParent;
        private readonly Entity _newParent;

        public SetParentCommand(Entity entity, Entity oldParent, Entity newParent)
        {
            _entity = entity;
            _oldParent = oldParent;
            _newParent = newParent;
        }

        public void Execute()
        {
            if (_newParent != null)
                _newParent.AddChild(_entity);
            else
                _entity.Parent?.RemoveChild(_entity);
        }

        public void Undo()
        {
            if (_oldParent != null)
                _oldParent.AddChild(_entity);
            else
                _entity.Parent?.RemoveChild(_entity);
        }
    }
}

using System.Diagnostics;
using RockEngine.Core.ECS;

namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public class ReparentEntityCommand : IUndoRedoCommand
    {
        private readonly Entity _entity;
        private readonly Entity _oldParent;
        private readonly Entity _newParent;

        public ReparentEntityCommand(Entity entity, Entity newParent)
        {
            _entity = entity;
            _oldParent = entity.Parent;
            _newParent = newParent;
        }

        public void Execute()
        {
            // Detach from old parent
            _oldParent?.RemoveChild(_entity);

            // Attach to new parent
            _newParent?.AddChild(_entity);
        }

        public void Undo()
        {
            // Detach from new parent
            _newParent?.RemoveChild(_entity);

            // Reattach to old parent
            _oldParent?.AddChild(_entity);
        }

        private string GetDebuggerDisplay()
        {
            return $"Entity: {_entity.Name}; oldParent:{_oldParent?.Name}; newParent: {_newParent?.Name}";
        }
    }
}

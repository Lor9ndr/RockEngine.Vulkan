using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;


namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class RemoveComponentCommand : IUndoRedoCommand
    {
        private readonly Entity _entity;
        private readonly IComponent _component;

        public RemoveComponentCommand(Entity entity, IComponent component)
        {
            _entity = entity;
            _component = component;
        }

        public void Execute()
        {
            _entity.RemoveComponent(_component);
        }

        public void Undo()
        {
            // Re-add the exact same component instance
            _entity.AddComponent(_component);
        }
    }
}
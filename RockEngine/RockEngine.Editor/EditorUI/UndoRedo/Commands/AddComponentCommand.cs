using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;


namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class AddComponentCommand : IUndoRedoCommand
    {
        private readonly Entity _entity;
        private readonly Type _componentType;
        private IComponent _addedComponent;

        public AddComponentCommand(Entity entity, Type componentType)
        {
            _entity = entity;
            _componentType = componentType;
        }

        public void Execute()
        {
            _addedComponent = _entity.AddComponent(_componentType);
        }

        public void Undo()
        {
            if (_addedComponent != null)
            {
                _entity.RemoveComponent(_addedComponent);
            }
        }
    }
}

using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;


namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class ChangePropertyCommand<T> : IUndoRedoCommand
    {
        private readonly IComponent _target;
        private readonly UIPropertyAccessor _propertyAccessor;
        private readonly T _oldValue;
        private readonly T _newValue;

        public ChangePropertyCommand(IComponent target, UIPropertyAccessor propertyAccessor, T oldValue, T newValue)
        {
            _target = target;
            _propertyAccessor = propertyAccessor;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Execute()
        {
            SetValue(_newValue);
        }

        public void Undo()
        {
            SetValue(_oldValue);
        }

        private void SetValue(T value)
        {
            _propertyAccessor.SetValue(_target, value);
        }
    }
}
using RockEngine.Core.Assets;

namespace RockEngine.Editor.EditorUI.UndoRedo.Commands
{
    public class ChangeMaterialParameterCommand : IUndoRedoCommand
    {
        private readonly MaterialAsset _material;
        private readonly string _parameterName;
        private readonly object _oldValue;
        private readonly object _newValue;

        public ChangeMaterialParameterCommand(MaterialAsset material, string parameterName, object oldValue, object newValue)
        {
            _material = material;
            _parameterName = parameterName;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Execute()
        {
            _material.UpdateParameter(_parameterName, _newValue);
        }

        public void Undo()
        {
            _material.UpdateParameter(_parameterName, _oldValue);
        }
    }
}
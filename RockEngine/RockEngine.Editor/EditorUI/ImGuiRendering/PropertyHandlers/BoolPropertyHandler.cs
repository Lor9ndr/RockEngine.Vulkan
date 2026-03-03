using ImGuiNET;

using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(bool))]
    public class BoolPropertyHandler : BasePropertyHandler<bool>
    {
        protected override void DrawProperty(IComponent component, UIPropertyAccessor accessor, bool value, PropertyDrawer drawer)
        {
            bool oldValue = value;
            if (ImGui.Checkbox(accessor.DisplayName, ref value) && accessor.CanWrite)
            {
                accessor.SetValue(component, value);
                var cmd = new ChangePropertyCommand<bool>(component, accessor, oldValue, value);
                UndoRedoService.Instance.Execute(cmd);
            }
        }
    }
}
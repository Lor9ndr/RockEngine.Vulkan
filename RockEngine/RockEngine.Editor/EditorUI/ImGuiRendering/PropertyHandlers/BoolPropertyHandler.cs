using ImGuiNET;

using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(bool))]
    public class BoolPropertyHandler : BasePropertyHandler<bool>
    {
        protected override void DrawProperty(IComponent component, UIPropertyAccessor accessor, bool value, PropertyDrawer drawer)
        {
            if (ImGui.Checkbox(accessor.DisplayName, ref value) && accessor.CanWrite)
            {
                accessor.SetValue(component, value);
            }
        }
    }
}

using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(int))]
    public class IntPropertyHandler : BasePropertyHandler<int>
    {
        protected override ValueTask DrawProperty(IComponent component, UIPropertyAccessor accessor, int value, PropertyDrawer drawer)
        {
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 1f; // Default step of 1 for integers

            if (ImGui.DragInt(accessor.DisplayName, ref value, step) && accessor.CanWrite)
            {
                accessor.SetValue(component, value);
            }
            return ValueTask.CompletedTask;
        }
    }

}

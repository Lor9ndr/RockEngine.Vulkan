using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{

    [PropertyHandler(typeof(float))]
    public class FloatPropertyHandler : BasePropertyHandler<float>
    {
        protected override ValueTask DrawProperty(IComponent component, UIPropertyAccessor accessor, float value, PropertyDrawer drawer)
        {
            var range = accessor.GetAttribute<RangeAttribute>();
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 0.1f; // Default step of 0.1 if not provided

            if (range != null)
            {
                ImGui.DragFloat(accessor.DisplayName, ref value, step, range.Min, range.Max);
            }
            else
            {
                ImGui.DragFloat(accessor.DisplayName, ref value, step);
            }

            if (accessor.CanWrite)
            {
                accessor.SetValue(component, value);
            }

            return ValueTask.CompletedTask;

        }
    }
}

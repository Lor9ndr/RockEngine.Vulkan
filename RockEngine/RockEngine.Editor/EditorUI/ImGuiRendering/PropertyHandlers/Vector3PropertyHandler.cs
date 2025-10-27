using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(Vector3))]
    public class Vector3PropertyHandler : BasePropertyHandler<Vector3>
    {
        protected override ValueTask DrawProperty(IComponent component, UIPropertyAccessor accessor, Vector3 value, PropertyDrawer drawer)
        {
            bool isColor = accessor.GetAttribute<ColorAttribute>() != null;
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 0.1f;

            if (isColor)
            {
                ImGui.ColorEdit3(accessor.DisplayName, ref value);
            }
            else
            {
                ImGui.DragFloat3(accessor.DisplayName, ref value, step);
            }

            if (accessor.CanWrite)
            {
                accessor.SetValue(component, value);
            }
            return ValueTask.CompletedTask;
        }
    }

}

using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(Vector2))]
    public class Vector2PropertyHandler : BasePropertyHandler<Vector2>
    {
        protected override void DrawProperty(IComponent component, UIPropertyAccessor accessor, Vector2 value, PropertyDrawer drawer)
        {
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 0.1f;

            ImGui.DragFloat2(accessor.DisplayName, ref value, step);

            if (accessor.CanWrite)
            {
                accessor.SetValue(component, value);
            }

        }
    }

}

using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

using System.Collections.Generic;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(float))]
    public class FloatPropertyHandler : BasePropertyHandler<float>
    {
        private readonly Dictionary<string, float> _editingOldValues = new();

        protected override void DrawProperty(IComponent component, UIPropertyAccessor accessor, float value, PropertyDrawer drawer)
        {
            var range = accessor.GetAttribute<RangeAttribute>();
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 0.1f;

            string controlId = $"{component.GetHashCode()}_{accessor.Name}";
            float currentValue = value;

            // Draw control
            if (range != null)
                ImGui.DragFloat(accessor.DisplayName, ref value, step, range.Min, range.Max);
            else
                ImGui.DragFloat(accessor.DisplayName, ref value, step);

            if (ImGui.IsItemActivated())
                _editingOldValues[controlId] = currentValue;

            if (ImGui.IsItemActive() && accessor.CanWrite)
                accessor.SetValue(component, value);

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (_editingOldValues.TryGetValue(controlId, out var oldValue))
                {
                    var cmd = new ChangePropertyCommand<float>(component, accessor, oldValue, value);
                    UndoRedoService.Instance.Execute(cmd);
                    _editingOldValues.Remove(controlId);
                }
            }
        }
    }
}
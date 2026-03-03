using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

using System.Collections.Generic;
using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(Vector3))]
    public class Vector3PropertyHandler : BasePropertyHandler<Vector3>
    {
        // Dictionary to store the original value while editing
        private readonly Dictionary<string, Vector3> _editingOldValues = new();

        protected override void DrawProperty(IComponent component, UIPropertyAccessor accessor, Vector3 value, PropertyDrawer drawer)
        {
            bool isColor = accessor.GetAttribute<ColorAttribute>() != null;
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 0.1f;

            // Unique ID for this control
            string controlId = $"{component.GetHashCode()}_{accessor.Name}";

            // Capture the current value before drawing (may be needed if activation happens this frame)
            Vector3 currentValue = value;

            // Draw the control
            if (isColor)
            {
                ImGui.ColorEdit3(accessor.DisplayName, ref value);
            }
            else
            {
                ImGui.DragFloat3(accessor.DisplayName, ref value, step);
            }

            // Check if the control became active this frame → store the original value
            if (ImGui.IsItemActivated())
            {
                _editingOldValues[controlId] = currentValue;
            }

            // Live update while dragging
            if (ImGui.IsItemActive() && accessor.CanWrite)
            {
                accessor.SetValue(component, value);
            }

            // Edit finished: push command with the stored original value
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (_editingOldValues.TryGetValue(controlId, out var oldValue))
                {
                    var cmd = new ChangePropertyCommand<Vector3>(component, accessor, oldValue, value);
                    UndoRedoService.Instance.Execute(cmd);
                    _editingOldValues.Remove(controlId);
                }
            }
        }
    }
}
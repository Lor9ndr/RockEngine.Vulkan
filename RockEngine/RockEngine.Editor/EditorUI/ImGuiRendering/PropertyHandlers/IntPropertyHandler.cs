using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

using System.Collections.Generic;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(int))]
    public class IntPropertyHandler : BasePropertyHandler<int>
    {
        private readonly Dictionary<string, int> _editingOldValues = new();

        protected override void DrawProperty(IComponent component, UIPropertyAccessor accessor, int value, PropertyDrawer drawer)
        {
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 1f;

            string controlId = $"{component.GetHashCode()}_{accessor.Name}";
            int currentValue = value;

            ImGui.DragInt(accessor.DisplayName, ref value, step);

            if (ImGui.IsItemActivated())
                _editingOldValues[controlId] = currentValue;

            if (ImGui.IsItemActive() && accessor.CanWrite)
                accessor.SetValue(component, value);

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (_editingOldValues.TryGetValue(controlId, out var oldValue))
                {
                    var cmd = new ChangePropertyCommand<int>(component, accessor, oldValue, value);
                    UndoRedoService.Instance.Execute(cmd);
                    _editingOldValues.Remove(controlId);
                }
            }
        }
    }
}
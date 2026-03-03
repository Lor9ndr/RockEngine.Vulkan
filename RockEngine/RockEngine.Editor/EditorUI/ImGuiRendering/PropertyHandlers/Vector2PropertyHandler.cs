using ImGuiNET;

using RockEngine.Core.Attributes;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

using System.Numerics;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(Vector2))]
    public class Vector2PropertyHandler : BasePropertyHandler<Vector2>
    {
        private readonly Dictionary<string, Vector2> _editingOldValues = new();

        protected override void DrawProperty(IComponent component, UIPropertyAccessor accessor, Vector2 value, PropertyDrawer drawer)
        {
            var stepAttr = accessor.GetAttribute<StepAttribute>();
            float step = stepAttr?.Step ?? 0.1f;

            string controlId = $"{component.GetHashCode()}_{accessor.Name}";
            Vector2 currentValue = value;

            ImGui.DragFloat2(accessor.DisplayName, ref value, step);

            if (ImGui.IsItemActivated())
                _editingOldValues[controlId] = currentValue;

            if (ImGui.IsItemActive() && accessor.CanWrite)
                accessor.SetValue(component, value);

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                if (_editingOldValues.TryGetValue(controlId, out var oldValue))
                {
                    var cmd = new ChangePropertyCommand<Vector2>(component, accessor, oldValue, value);
                    UndoRedoService.Instance.Execute(cmd);
                    _editingOldValues.Remove(controlId);
                }
            }
        }
    }
}
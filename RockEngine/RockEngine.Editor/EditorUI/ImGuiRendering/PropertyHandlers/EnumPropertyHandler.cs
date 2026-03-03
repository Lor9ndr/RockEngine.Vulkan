using ImGuiNET;

using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Editor.EditorUI.UndoRedo;
using RockEngine.Editor.EditorUI.UndoRedo.Commands;

using System.Collections.Generic;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(Enum))]
    public class EnumPropertyHandler : IPropertyHandler
    {
        private readonly Dictionary<string, Enum> _editingOldValues = new();

        public bool CanHandle(Type propertyType) => propertyType.IsEnum;

        public void Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            Enum enumValue = (Enum)value;
            string controlId = $"{component.GetHashCode()}_{accessor.Name}";

            if (ImGui.BeginCombo(accessor.DisplayName, enumValue.ToString()))
            {
                // When the combo opens, store the current value
                if (ImGui.IsWindowAppearing())
                    _editingOldValues[controlId] = enumValue;

                foreach (Enum enumVal in Enum.GetValues(accessor.PropertyType))
                {
                    bool isSelected = enumValue.Equals(enumVal);
                    if (ImGui.Selectable(enumVal.ToString(), isSelected))
                    {
                        if (accessor.CanWrite && !enumValue.Equals(enumVal))
                        {
                            accessor.SetValue(component, enumVal);
                            if (_editingOldValues.TryGetValue(controlId, out var oldValue))
                            {
                                var cmd = new ChangePropertyCommand<Enum>(component, accessor, oldValue, enumVal);
                                UndoRedoService.Instance.Execute(cmd);
                                _editingOldValues.Remove(controlId);
                            }
                        }
                    }
                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
    }
}
using ImGuiNET;

using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    [PropertyHandler(typeof(Enum))]
    public class EnumPropertyHandler : IPropertyHandler
    {
        public EnumPropertyHandler()
        {
        }

        public bool CanHandle(Type propertyType) => propertyType.IsEnum;

        public ValueTask Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            Enum enumValue = (Enum)value;
            if (ImGui.BeginCombo(accessor.DisplayName, enumValue.ToString()))
            {
                foreach (Enum enumVal in Enum.GetValues(accessor.PropertyType))
                {
                    bool isSelected = enumValue.Equals(enumVal);
                    if (ImGui.Selectable(enumVal.ToString(), isSelected) && accessor.CanWrite)
                    {
                        accessor.SetValue(component, enumVal);
                    }
                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
            return ValueTask.CompletedTask;
        }
    }
}

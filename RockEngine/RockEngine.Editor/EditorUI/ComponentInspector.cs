using ImGuiNET;

using RockEngine.Core.ECS.Components;
using RockEngine.Editor.UIAttributes;

using System.Numerics;
using System.Reflection;

namespace RockEngine.Editor.EditorUI
{
    public static class ComponentInspector
    {
        public static void DrawComponent(Component component)
        {
            Type type = component.GetType();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var uiAttr = field.GetCustomAttribute<UIEditableAttribute>();
                if (uiAttr == null) continue;

                string label = uiAttr.DisplayName ;//?? ObjectNames.NicifyVariableName(field.Name);
                object value = field.GetValue(component);

                DrawField(label, value, field, component);
            }
        }

        private static void DrawField(string label, object value, FieldInfo field, Component component)
        {
            switch (value)
            {
                case float f:
                    DrawFloatField(label, f, field, component);
                    break;
                case Vector3 vec when field.GetCustomAttribute<ColorAttribute>() != null:
                    DrawColorField(label, ref vec, field, component);
                    break;
                case Vector3 vec:
                    DrawVector3Field(label, ref vec, field, component);
                    break;
                    // Add more type handlers
            }
        }

        private static void DrawFloatField(string label, float value, FieldInfo field, Component component)
        {
            var rangeAttr = field.GetCustomAttribute<RangeAttribute>();
            if (rangeAttr != null)
            {
                float val = value;
                if (ImGui.SliderFloat(label, ref val, rangeAttr.Min, rangeAttr.Max))
                {
                    field.SetValue(component, val);
                }
            }
            else
            {
                ImGui.Text($"{label}: {value}");
            }
        }

        private static void DrawVector3Field(string label, ref Vector3 value, FieldInfo field, Component component)
        {
            Vector3 vec = new(value.X, value.Y, value.Z);
            if (ImGui.DragFloat3(label, ref vec))
            {
                value = new Vector3(vec.X, vec.Y, vec.Z);
                field.SetValue(component, value);
            }
        }

        private static void DrawColorField(string label, ref Vector3 value, FieldInfo field, Component component)
        {
            System.Numerics.Vector3 color = new(value.X, value.Y, value.Z);
            if (ImGui.ColorEdit3(label, ref color))
            {
                value = new Vector3(color.X, color.Y, color.Z);
                field.SetValue(component, value);
            }
        }
    }
}

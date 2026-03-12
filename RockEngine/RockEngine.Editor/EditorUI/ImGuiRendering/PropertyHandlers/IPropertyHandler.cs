using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;

namespace RockEngine.Editor.EditorUI.ImGuiRendering.PropertyHandlers
{
    public interface IPropertyHandler
    {
        bool CanHandle(Type propertyType);
        void Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer);
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PropertyHandlerAttribute : Attribute
    {
        public Type[] HandledTypes { get; }

        public PropertyHandlerAttribute(params Type[] handledTypes)
        {
            HandledTypes = handledTypes;
        }
    }

    public abstract class BasePropertyHandler<T> : IPropertyHandler
    {
        public virtual bool CanHandle(Type propertyType) => propertyType == typeof(T);

        public void Draw(IComponent component, UIPropertyAccessor accessor, object value, PropertyDrawer drawer)
        {
            if (value is T typedValue)
            {
                 DrawProperty(component, accessor, typedValue, drawer);
            }
        }

        protected abstract void DrawProperty(IComponent component, UIPropertyAccessor accessor, T value, PropertyDrawer drawer);
    }
}

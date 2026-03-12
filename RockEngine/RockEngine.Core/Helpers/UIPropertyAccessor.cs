using RockEngine.Core.ECS.Components;



namespace RockEngine.Core.Helpers
{
    public delegate object PropertyGetter(IComponent component);
    public delegate void PropertySetter(IComponent component, object value);

    public sealed class UIPropertyAccessor
    {
        public string Name { get; }
        public string DisplayName { get; }
        public Type PropertyType { get; }
        public PropertyGetter GetValue { get; }
        public PropertySetter SetValue { get; }
        public bool CanWrite { get; }
        public IReadOnlyList<Attribute> Attributes { get; }

        public UIPropertyAccessor(
            string name,
            string displayName,
            Type propertyType,
            PropertyGetter getValue,
            PropertySetter setValue,
            bool canWrite,
            Attribute[] attributes)
        {
            Name = name;
            DisplayName = displayName;
            PropertyType = propertyType;
            GetValue = getValue;
            SetValue = setValue;
            CanWrite = canWrite;
            Attributes = attributes ?? Array.Empty<Attribute>();
        }

        public T GetAttribute<T>() where T : Attribute
        {
            return Attributes.OfType<T>().FirstOrDefault();
        }
    }
}

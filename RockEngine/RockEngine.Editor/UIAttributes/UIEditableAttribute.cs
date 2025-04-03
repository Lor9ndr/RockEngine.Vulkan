namespace RockEngine.Editor.UIAttributes
{
    [AttributeUsage(AttributeTargets.Field)]
    public class UIEditableAttribute : Attribute
    {
        public string DisplayName { get; }
        public UIEditableAttribute(string displayName = null) => DisplayName = displayName;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class RangeAttribute : Attribute
    {
        public float Min { get; }
        public float Max { get; }
        public RangeAttribute(float min, float max) { Min = min; Max = max; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class ColorAttribute : Attribute { }
}
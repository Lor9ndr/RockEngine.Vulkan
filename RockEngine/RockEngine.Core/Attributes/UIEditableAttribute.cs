namespace RockEngine.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class UIEditableAttribute : Attribute
    {
        public string? DisplayName { get; }
        public UIEditableAttribute(string? displayName = null) => DisplayName = displayName;
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class RangeAttribute : Attribute
    {
        public float Min { get; }
        public float Max { get; }
        public RangeAttribute(float min, float max) { Min = min; Max = max; }
    }
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class RangeAttribute<T> : Attribute
    {
        public T Min { get; }
        public T Max { get; }
        public RangeAttribute(T min, T max) { Min = min; Max = max; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ColorAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class StepAttribute : Attribute
    {
        public float Step { get; }

        public StepAttribute(float step)
        {
            Step = step;
        }
    }
}
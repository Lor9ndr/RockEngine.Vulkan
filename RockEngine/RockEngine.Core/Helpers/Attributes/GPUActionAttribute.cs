namespace RockEngine.Core.Helpers.Attributes
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class GPUActionAttribute : Attribute
    {
        public float[] Color { get; }
        public string? CustomName { get; }

        public GPUActionAttribute(params float[] color)
        {
            Color = color;
        }

        public GPUActionAttribute(string customName, params float[] color)
        {
            CustomName = customName;
            Color = color;
        }
    }
}
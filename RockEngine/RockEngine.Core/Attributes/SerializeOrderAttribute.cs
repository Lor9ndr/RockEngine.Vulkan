namespace RockEngine.Core.Attributes
{
    /// <summary>
    /// Specifies the order of serialization (both JSON and binary)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class SerializeOrderAttribute : Attribute
    {
        public int Order { get; }

        public SerializeOrderAttribute(int order)
        {
            Order = order;
        }
    }
}

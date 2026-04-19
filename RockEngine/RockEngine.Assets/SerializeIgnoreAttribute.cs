namespace RockEngine.Core.Attributes
{
    /// <summary>
    /// Marks a property to be ignored during serialization (both JSON and binary)
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class SerializeIgnoreAttribute : Attribute { }

}


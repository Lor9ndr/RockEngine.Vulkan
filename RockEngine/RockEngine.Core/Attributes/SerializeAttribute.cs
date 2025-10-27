namespace RockEngine.Core.Attributes
{
    /// <summary>
    /// Attribute to mark private fields that should be serialized
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class SerializeAttribute : Attribute
    {
    }
}


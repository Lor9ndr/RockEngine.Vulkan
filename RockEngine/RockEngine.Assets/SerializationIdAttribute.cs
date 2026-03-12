namespace RockEngine.Assets;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class SerializationIdAttribute : Attribute
{
    public ulong Id { get; }
    public SerializationIdAttribute(ulong id) => Id = id;
}

using MessagePack;
using MessagePack.Formatters;

namespace RockEngine.Assets;

public sealed class PolymorphicResolver : IFormatterResolver
{
    public static readonly PolymorphicResolver Instance = new();

    public IMessagePackFormatter<T>? GetFormatter<T>()
    {
        // We only intercept interface types that are polymorphic.
        // Concrete types should be handled by generated formatters (RockEngineResolver).
        var type = typeof(T);
        if (type.IsInterface && typeof(IPolymorphicSerializable).IsAssignableFrom(type))
        {
            // PolymorphicSerializableFormatter is non-generic; cast is safe.
            return (IMessagePackFormatter<T>)PolymorphicSerializableFormatter.Instance;
        }

        return null; // Fall through to next resolver.
    }
}
public sealed class PolymorphicSerializableFormatter : IMessagePackFormatter<IPolymorphicSerializable?>
{
    public static readonly IMessagePackFormatter<IPolymorphicSerializable?> Instance = new PolymorphicSerializableFormatter();

    public void Serialize(ref MessagePackWriter writer, IPolymorphicSerializable? value, MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        var type = value.GetType();
        if (!PolymorphicTypeRegistry.Instance.TryGetId(type, out var id))
            throw new NotSupportedException($"Type {type} is not registered in polymorphic registry.");

        // Write header: [typeId, object]
        writer.WriteArrayHeader(2);
        writer.Write(id);

       
        MessagePackSerializer.Serialize(type, ref writer, value, options);
    }

    public IPolymorphicSerializable? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        options.Security.DepthStep(ref reader);
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new InvalidOperationException("Invalid polymorphic format.");

        var id = reader.ReadUInt64();
        if (!PolymorphicTypeRegistry.Instance.TryGetType(id, out var type))
            throw new NotSupportedException($"Unknown polymorphic type ID: {id}");

        var result = MessagePackSerializer.Deserialize(type, ref reader, options);
        reader.Depth--;
        return (IPolymorphicSerializable?)result;
    }
}
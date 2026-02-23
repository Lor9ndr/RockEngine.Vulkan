using System.Collections.Concurrent;
using System.Reflection;

namespace RockEngine.Assets;

public sealed class PolymorphicTypeRegistry
{
    private readonly ConcurrentDictionary<Type, ulong> _typeToId = new();
    private readonly ConcurrentDictionary<ulong, Type> _idToType = new();

    public void Register<T>() where T : IPolymorphicSerializable => Register(typeof(T));

    public void Register(Type type)
    {
        if (!typeof(IPolymorphicSerializable).IsAssignableFrom(type))
            throw new ArgumentException($"Type {type} does not implement IPolymorphicSerializable");

        if (type.IsAbstract || type.IsInterface)
            return; // Only concrete types are registered

        ulong id = GetId(type);
        _typeToId[type] = id;
        _idToType[id] = type;
    }

    private ulong GetId(Type type)
    {
        var attr = type.GetCustomAttribute<SerializationIdAttribute>();
        return attr?.Id ?? TypeIdGenerator.GetStableId(type);
    }

    public bool TryGetId(Type type, out ulong id) => _typeToId.TryGetValue(type, out id);
    public bool TryGetType(ulong id, out Type type) => _idToType.TryGetValue(id, out type);

    // Singleton
    public static readonly PolymorphicTypeRegistry Instance = new();
    public static void AutoRegisterAll()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic);

        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                if (typeof(IPolymorphicSerializable).IsAssignableFrom(type) &&
                    !type.IsAbstract && !type.IsInterface)
                {
                    Instance.Register(type);
                }
            }
        }
    }
}

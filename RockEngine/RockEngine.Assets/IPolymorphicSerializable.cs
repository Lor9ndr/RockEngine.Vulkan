using MessagePack;

using System.Text;

namespace RockEngine.Assets;

/// <summary>
///  Marker interface – all types that need polymorphic serialization must implement it.
/// </summary>
public interface IPolymorphicSerializable
{
}
public static class TypeIdGenerator
{
    public static ulong GetStableId(Type type)
    {
        // Use full name without version/culture to ensure stability across builds
        string fullName = type.FullName ?? type.Name;
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fullName));
        // Take first 8 bytes (64 bits) – very low collision probability for < 1M types
        return BitConverter.ToUInt64(hash, 0);
    }
}

namespace RockEngine.Core.Assets.Converters
{
    /// <summary>
    /// Base interface for custom serialization converters
    /// </summary>
    public interface ISerializationConverter
    {
        object ConvertToSerializable(object value);
        object ConvertFromSerializable(object value);
    }
}

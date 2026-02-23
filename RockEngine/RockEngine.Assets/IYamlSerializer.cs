namespace RockEngine.Assets
{
    /// <summary>
    /// Interface for YAML serialization
    /// </summary>
    public interface IYamlSerializer
    {
        Task SerializeAsync(object data, Stream stream);
        Task<object> DeserializeAsync(Stream stream, Type type);
    }
}

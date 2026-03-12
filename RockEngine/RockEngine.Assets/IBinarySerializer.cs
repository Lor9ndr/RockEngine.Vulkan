namespace RockEngine.Assets
{
    /// <summary>
    /// Interface for binary serialization
    /// </summary>
    public interface IBinarySerializer
    {
        Task SerializeAsync<T>(T data, Stream stream);
        Task SerializeAsync(object data, Type type, Stream stream);
        Task<object> DeserializeAsync(Stream stream, Type type);
    }
}

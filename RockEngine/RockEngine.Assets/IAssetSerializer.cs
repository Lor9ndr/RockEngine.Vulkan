namespace RockEngine.Assets
{
    /// <summary>
    /// Simplified IAssetSerializer interface that handles metadata within the asset file
    /// </summary>
    public interface IAssetSerializer
    {
        Task SerializeAsync(IAsset asset, Stream stream);
        Task<AssetHeader> DeserializeHeaderAsync(Stream stream);
        Task<object> DeserializeDataAsync(Stream stream, Type dataType);
        Task<IAsset> DeserializeAssetAsync(Stream stream, AssetPath path);
        Task<Type> GetAssetTypeAsync(Stream stream);
    }

    /// <summary>
    /// Asset header containing metadata (stored at beginning of asset file)
    /// </summary>
    public class AssetHeader
    {
        public AssetHeader()
        {
        }

        public AssetHeader(Guid assetId, string assetTypeName, string name, DateTime created, DateTime modified, int version, string format)
        {
            AssetId = assetId;
            AssetTypeName = assetTypeName;
            Name = name;
            Created = created;
            Modified = modified;
            Version = version;
            Format = format;
        }

        public Guid AssetId { get; set; }
        public string AssetTypeName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public int Version { get; set; } = 1;
        public string Format { get; set; } = "yaml";

        [System.Text.Json.Serialization.JsonIgnore]
        public Type? AssetType => Type.GetType(AssetTypeName);
    }

    /// <summary>
    /// Asset wrapper that contains both metadata and data
    /// </summary>
    public class AssetWrapper
    {
        public AssetHeader Header { get; set; } = new AssetHeader();
        public object Data { get; set; } = new object();
    }
}
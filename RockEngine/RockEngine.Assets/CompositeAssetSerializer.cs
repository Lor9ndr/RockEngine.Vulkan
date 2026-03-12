using System.Text;

namespace RockEngine.Assets
{
    /// <summary>
    /// Composite asset serializer that handles metadata within the same file
    /// </summary>
    public class CompositeAssetSerializer : IAssetSerializer
    {
        private readonly List<IAssetSerializationStrategy> _strategies;
        private readonly IAssetFactory _assetFactory;

        public CompositeAssetSerializer(
            IEnumerable<IAssetSerializationStrategy> strategies,
            IAssetFactory assetFactory)
        {
            _strategies = strategies.ToList();
            _assetFactory = assetFactory;
        }

        public async Task SerializeAsync(IAsset asset, Stream stream)
        {
            var strategy = GetStrategyForAsset(asset);
            await strategy.SerializeAsync(asset, stream);
        }

        public async Task<AssetHeader> DeserializeHeaderAsync(Stream stream)
        {
            var originalPosition = stream.Position;

            try
            {
                // Try to read as YAML first
                stream.Position = 0;
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var firstLine = await reader.ReadLineAsync();

                if (firstLine != null && firstLine.StartsWith("# ROCK Asset"))
                {
                    // YAML format with comments header
                    return await DeserializeYamlHeaderAsync(stream);
                }
                else
                {
                    // Try binary format
                    return await DeserializeBinaryHeaderAsync(stream);
                }
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        public async Task<object> DeserializeDataAsync(Stream stream, Type dataType)
        {
            var header = await DeserializeHeaderAsync(stream);
            stream.Position = 0; // Reset stream

            var strategy = GetStrategyByFormat(header.Format);
            var tempPath = new AssetPath("temp");

            // Create asset instance
            var assetType = Type.GetType(header.AssetTypeName) ??
                throw new TypeLoadException($"Cannot load asset type: {header.AssetTypeName}");

            var asset = _assetFactory.Create(tempPath, assetType);
            asset.ID = header.AssetId;
            asset.Name = header.Name;
            asset.Created = header.Created;
            asset.Modified = header.Modified;

            // Deserialize data
            await strategy.DeserializeDataAsync(asset, stream);
            return asset.GetData();
        }

        public async Task<IAsset> DeserializeAssetAsync(Stream stream, AssetPath path)
        {
            var header = await DeserializeHeaderAsync(stream);
            stream.Position = 0; // Reset stream

            var strategy = GetStrategyByFormat(header.Format);

            // Create and populate asset
            var assetType = Type.GetType(header.AssetTypeName) ??
                throw new TypeLoadException($"Cannot load asset type: {header.AssetTypeName}");

            var asset = _assetFactory.Create(path, assetType);
            asset.ID = header.AssetId;
            asset.Name = header.Name;
            asset.Created = header.Created;
            asset.Modified = header.Modified;

            // Deserialize the full asset
            await strategy.DeserializeDataAsync(asset, stream);
            return asset;
        }

        public async Task<Type> GetAssetTypeAsync(Stream stream)
        {
            var header = await DeserializeHeaderAsync(stream);
            return Type.GetType(header.AssetTypeName) ??
                throw new TypeLoadException($"Cannot load asset type: {header.AssetTypeName}");
        }

        private async Task<AssetHeader> DeserializeYamlHeaderAsync(Stream stream)
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            var header = new AssetHeader { Format = "yaml" };
            string line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("# ID: "))
                    header.AssetId = Guid.Parse(line.AsSpan("# ID: ".Length));
                else if (line.StartsWith("# Type: "))
                    header.AssetTypeName = line["# Type: ".Length..];
                else if (line.StartsWith("# Name: "))
                    header.Name = line["# Name: ".Length..];
                else if (line.StartsWith("# Created: "))
                    header.Created = DateTime.Parse(line.Substring("# Created: ".Length));
                else if (line.StartsWith("# Modified: "))
                    header.Modified = DateTime.Parse(line.Substring("# Modified: ".Length));
                else if (line == "---")
                    break; // End of header
            }

            return header;
        }

        private async Task<AssetHeader> DeserializeBinaryHeaderAsync(Stream stream)
        {
            stream.Position = 0;
            var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            // Check magic number
            var magic = reader.ReadInt32();
            if (magic != 0x524F434B) // "ROCK"
                throw new InvalidDataException("Invalid binary asset format");

            var header = new AssetHeader { Format = "binary" };

            // Read header fields
            header.Version = reader.ReadInt32();
            header.AssetId = new Guid(reader.ReadBytes(16));

            var typeNameLength = reader.ReadInt32();
            header.AssetTypeName = Encoding.UTF8.GetString(reader.ReadBytes(typeNameLength));

            var nameLength = reader.ReadInt32();
            header.Name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));

            var createdTicks = reader.ReadInt64();
            header.Created = new DateTime(createdTicks, DateTimeKind.Utc);

            var modifiedTicks = reader.ReadInt64();
            header.Modified = new DateTime(modifiedTicks, DateTimeKind.Utc);

            return header;
        }

        private IAssetSerializationStrategy GetStrategyForAsset(IAsset asset)
        {
            var assetType = asset.GetType();

            foreach (var strategy in _strategies)
            {
                if (strategy.CanHandle(assetType))
                    return strategy;
            }

            return _strategies.First();
        }

        private IAssetSerializationStrategy GetStrategyByFormat(string format)
        {
            return format.ToLowerInvariant() switch
            {
                "binary" => _strategies.First(s => s is BinaryAssetSerializationStrategy),
                _ => _strategies.First(s => s is YamlAssetSerializationStrategy)
            };
        }
    }
}
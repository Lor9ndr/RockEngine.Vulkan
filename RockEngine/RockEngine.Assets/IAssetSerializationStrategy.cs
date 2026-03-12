using System.Text;

namespace RockEngine.Assets
{
    /// <summary>
    /// Updated serialization strategies that write metadata in the file
    /// </summary>
    public interface IAssetSerializationStrategy
    {
        bool CanHandle(Type assetType);
        Task SerializeAsync(IAsset asset, Stream stream);
        Task DeserializeDataAsync(IAsset asset, Stream stream);
    }

    /// <summary>
    /// YAML serialization strategy with embedded metadata
    /// </summary>
    public class YamlAssetSerializationStrategy : IAssetSerializationStrategy
    {
        private readonly IYamlSerializer _yamlSerializer;

        public YamlAssetSerializationStrategy(IYamlSerializer yamlSerializer)
        {
            _yamlSerializer = yamlSerializer;
        }

        public bool CanHandle(Type assetType) => true; // Handles all assets

        public async Task SerializeAsync(IAsset asset, Stream stream)
        {
            // Записываем заголовок напрямую без StreamWriter
            var header = $"# ROCK Asset\n" +
                        $"# ID: {asset.ID}\n" +
                        $"# Type: {asset.GetType().AssemblyQualifiedName}\n" +
                        $"# Name: {asset.Name}\n" +
                        $"# Created: {asset.Created:o}\n" +
                        $"# Modified: {DateTime.UtcNow:o}\n" +
                        $"---\n";

            var headerBytes = Encoding.UTF8.GetBytes(header);
            await stream.WriteAsync(headerBytes);

            // Write asset data
            var data = asset.GetData();
            await _yamlSerializer.SerializeAsync(data, stream);
        }

        public async Task DeserializeDataAsync(IAsset asset, Stream stream)
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            // Skip the header (first 7 lines)
            for (int i = 0; i < 7; i++)
                await reader.ReadLineAsync();

            // Читаем остаток YAML
            var yamlContent = await reader.ReadToEndAsync();

            // Десериализуем из строки
            var dataType = asset.GetDataType();


            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(yamlContent));
            var data = await _yamlSerializer.DeserializeAsync(memoryStream, dataType);
            asset.SetData(data);
        }
    }

    /// <summary>
    /// Binary serialization strategy with embedded metadata
    /// </summary>
    public class BinaryAssetSerializationStrategy : IAssetSerializationStrategy
    {
        private readonly IBinarySerializer _binarySerializer;

        public BinaryAssetSerializationStrategy(IBinarySerializer binarySerializer)
        {
            _binarySerializer = binarySerializer;
        }

        public bool CanHandle(Type assetType) =>
            true;

        public async Task SerializeAsync(IAsset asset, Stream stream)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                // Write header
                writer.Write(0x524F434B); // Magic number "ROCK"
                writer.Write(1); // Version
                writer.Write(asset.ID.ToByteArray());

                var typeName = asset.GetType().AssemblyQualifiedName ?? string.Empty;
                var typeNameBytes = Encoding.UTF8.GetBytes(typeName);
                writer.Write(typeNameBytes.Length);
                writer.Write(typeNameBytes);

                var nameBytes = Encoding.UTF8.GetBytes(asset.Name);
                writer.Write(nameBytes.Length);
                writer.Write(nameBytes);

                writer.Write(asset.Created.Ticks);
                writer.Write(DateTime.UtcNow.Ticks); // Modified
            } // BinaryWriter освобождается здесь, поток остается открытым

            // Write asset data
            var data = asset.GetData();
            await _binarySerializer.SerializeAsync(data,asset.GetDataType(), stream);
        }

        public async Task DeserializeDataAsync(IAsset asset, Stream stream)
        {
            // Устанавливаем позицию в начало
            stream.Position = 0;

            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                // Читаем и проверяем магическое число
                var magic = reader.ReadInt32();
                if (magic != 0x524F434B)
                    throw new InvalidDataException("Invalid binary asset format");

                // Пропускаем остальные поля заголовка
                reader.ReadInt32(); // Version
                reader.ReadBytes(16); // GUID

                var typeNameLength = reader.ReadInt32();
                reader.ReadBytes(typeNameLength); // Type name

                var nameLength = reader.ReadInt32();
                reader.ReadBytes(nameLength); // Name

                reader.ReadInt64(); // Created ticks
                reader.ReadInt64(); // Modified ticks
            }

            // Теперь поток находится в позиции после заголовка
            var dataType = asset.GetDataType();
            var data = await _binarySerializer.DeserializeAsync(stream, dataType);
            asset.SetData(data);
        }
    }
}
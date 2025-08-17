/*using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Serialization;

using System.Buffers;
using System.Text;

namespace RockEngine.Core.Assets.Serializers
{
    public sealed class BsonAssetSerializer : IAssetSerializer
    {
        private const string MAGIC_HEADER = "RASS";
        private const ushort VERSION = 1;
        private const int HEADER_SIZE = 6; // 4 (magic) + 2 (version)

        private readonly JsonSerializer _serializer;

        public BsonAssetSerializer(IEnumerable<JsonConverter> converters)
        {
            _serializer = new JsonSerializer
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                TypeNameHandling = TypeNameHandling.Auto,
                ContractResolver = new DefaultContractResolver
                {
                    IgnoreSerializableInterface = true
                }
            };

            foreach (var converter in converters)
            {
                _serializer.Converters.Add(converter);
            }
        }

        public void Serialize(IAsset asset, AssetMetadata metadata, Stream stream)
        {
            // Write header
            var headerBuffer = ArrayPool<byte>.Shared.Rent(HEADER_SIZE);
            try
            {
                Encoding.ASCII.GetBytes(MAGIC_HEADER).CopyTo(headerBuffer, 0);
                BitConverter.GetBytes(VERSION).CopyTo(headerBuffer, 4);
                stream.Write(headerBuffer, 0, HEADER_SIZE);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBuffer);
            }

            // Serialize metadata
            SerializeBson(metadata, stream);

            // Serialize asset data
            SerializeBson(asset.GetData(), stream);
        }

        private void SerializeBson(object value, Stream stream)
        {
            using var memoryStream = new MemoryStream();
            using (var writer = new BsonDataWriter(memoryStream))
            {
                writer.CloseOutput = false;
                _serializer.Serialize(writer, value);
            }

            var lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
            try
            {
                var length = (int)memoryStream.Length;
                BitConverter.GetBytes(length).CopyTo(lengthBuffer, 0);
                stream.Write(lengthBuffer, 0, 4);
                memoryStream.Position = 0;
                memoryStream.CopyTo(stream);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(lengthBuffer);
            }
        }

        public AssetMetadata DeserializeMetadata(Stream stream)
        {
            // Read header
            byte[] header = new byte[6];
            if (stream.Read(header) != 6) // Ensure full read
                throw new InvalidOperationException("Invalid header");

            // Skip position reset - stream is consumed
            byte[] lenBytes = new byte[4];
            if (stream.Read(lenBytes) != 4)
                throw new EndOfStreamException();

            int metadataLength = BitConverter.ToInt32(lenBytes, 0);
            byte[] metadataBytes = new byte[metadataLength];
            if (stream.Read(metadataBytes) != metadataLength)
                throw new EndOfStreamException();

            using var ms = new MemoryStream(metadataBytes);
            using var reader = new BsonDataReader(ms);
            return _serializer.Deserialize<AssetMetadata>(reader)!;
        }

        public object DeserializeDataAsync(Stream stream, Type dataType)
        {
            long originalPosition = stream.Position;
            try
            {
                // Skip header
                stream.Seek(HEADER_SIZE, SeekOrigin.Current);

                // Read metadata length and skip metadata
                var lengthBuffer = new byte[4];
                ReadExactly(stream, lengthBuffer);
                int metadataLength = BitConverter.ToInt32(lengthBuffer, 0);
                stream.Seek(metadataLength, SeekOrigin.Current);

                // Read data length
                ReadExactly(stream, lengthBuffer);
                int dataLength = BitConverter.ToInt32(lengthBuffer, 0);

                // Read data
                var dataBuffer = ArrayPool<byte>.Shared.Rent(dataLength);
                try
                {
                    ReadExactly(stream, dataBuffer, 0, dataLength);
                    using var ms = new MemoryStream(dataBuffer, 0, dataLength);
                    using var reader = new BsonDataReader(ms);
                    return _serializer.Deserialize(reader, dataType);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(dataBuffer);
                }
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset = 0, int? count = null)
        {
            int toRead = count ?? buffer.Length;
            int totalRead = 0;

            while (totalRead < toRead)
            {
                int read = stream.Read(buffer, offset + totalRead, toRead - totalRead);
                if (read == 0) throw new EndOfStreamException();
                totalRead += read;
            }
        }

      *//*  private static void ReadExactlyAsync(Stream stream, byte[] buffer, int offset = 0, int? count = null)
        {
            int toRead = count ?? buffer.Length;
            int totalRead = 0;

            while (totalRead < toRead)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, toRead - totalRead);
                if (read == 0) throw new EndOfStreamException();
                totalRead += read;
            }
        }*//*

        
    }
}*/
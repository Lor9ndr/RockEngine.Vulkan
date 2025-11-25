using System.Text.Json;
using System.Text.Json.Serialization;

namespace RockEngine.Core.Assets.Converters
{
    public class AssetReferenceConverter2 : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert.IsGenericType &&
                   typeToConvert.GetGenericTypeDefinition() == typeof(AssetReference<>);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type assetType = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(AssetReferenceConverter<>).MakeGenericType(assetType);
            return (JsonConverter)Activator.CreateInstance(converterType);
        }

        private class AssetReferenceConverter<T> : JsonConverter<AssetReference<T>> where T : class, IAsset
        {
            public override AssetReference<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                {
                    return null;
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    string guidString = reader.GetString();
                    if (Guid.TryParse(guidString, out Guid assetId))
                    {
                        return new AssetReference<T>(assetId);
                    }
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    using var document = JsonDocument.ParseValue(ref reader);
                    if (document.RootElement.TryGetProperty("AssetID", out var assetIdElement))
                    {
                        if (assetIdElement.ValueKind == JsonValueKind.String)
                        {
                            string guidString = assetIdElement.GetString();
                            if (Guid.TryParse(guidString, out Guid assetId))
                            {
                                return new AssetReference<T>(assetId);
                            }
                        }
                    }
                }

                return null;
            }

            public override void Write(Utf8JsonWriter writer, AssetReference<T> value, JsonSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStringValue(value.AssetID.ToString());
            }
        }
    }
}

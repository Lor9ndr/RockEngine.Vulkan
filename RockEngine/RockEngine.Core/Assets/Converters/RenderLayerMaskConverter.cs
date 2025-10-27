using Newtonsoft.Json;

using RockEngine.Core.Rendering;

using System;
using System.Numerics;

namespace RockEngine.Core.Assets.Converters
{
    public class RenderLayerMaskConverter : JsonConverter<RenderLayerMask>
    {
        public override void WriteJson(JsonWriter writer, RenderLayerMask value, JsonSerializer serializer)
        {
            writer.WriteValue((ulong)value);
        }

        public override RenderLayerMask ReadJson(JsonReader reader, Type objectType, RenderLayerMask existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var stringValue = reader.Value.ToString();
                if (ulong.TryParse(stringValue, out ulong numericValue))
                {
                    return (RenderLayerMask)numericValue;
                }

                // Try to parse as enum name
                if (Enum.TryParse<RenderLayerMask>(stringValue, out var enumValue))
                {
                    return enumValue;
                }
            }
            else if (reader.TokenType == JsonToken.Integer)
            {
                try
                {
                    ulong numericValue;

                    // Handle BigInteger specifically
                    if (reader.Value is BigInteger bigInt)
                    {
                        // Check if the BigInteger value is within ulong range
                        if (bigInt >= 0 && bigInt <= ulong.MaxValue)
                        {
                            numericValue = (ulong)bigInt;
                        }
                        else
                        {
                            // If out of range, clamp to valid range
                            numericValue = bigInt > ulong.MaxValue ? ulong.MaxValue : 0;
                        }
                    }
                    else
                    {
                        // For other integer types, use Convert
                        numericValue = Convert.ToUInt64(reader.Value);
                    }

                    return (RenderLayerMask)numericValue;
                }
                catch (Exception ex)
                {
                    // Fallback: try to parse as string
                    try
                    {
                        var stringValue = reader.Value.ToString();
                        if (ulong.TryParse(stringValue, out ulong fallbackValue))
                        {
                            return (RenderLayerMask)fallbackValue;
                        }
                    }
                    catch
                    {
                        // If all else fails, return None
                        return RenderLayerMask.None;
                    }
                }
            }

            return RenderLayerMask.None;
        }
    }
}
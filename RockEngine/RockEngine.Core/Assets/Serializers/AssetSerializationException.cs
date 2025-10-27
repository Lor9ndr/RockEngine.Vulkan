namespace RockEngine.Core.Assets.Serializers
{
    public class AssetSerializationException : Exception
    {
        public AssetSerializationException(string message, Exception inner)
            : base(message, inner) { }

        public AssetSerializationException(string message)
            : base(message) { }
    }
}
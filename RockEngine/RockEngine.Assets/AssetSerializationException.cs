namespace RockEngine.Assets
{
    [Serializable]
    internal class AssetSerializationException : Exception
    {
        public AssetSerializationException()
        {
        }

        public AssetSerializationException(string? message) : base(message)
        {
        }

        public AssetSerializationException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
namespace RockEngine.Core.Assets
{
    namespace RockEngine.Core.Assets.AssetData
{
        /// <summary>
        /// Represents texture comparison functions (for depth textures)
        /// </summary>
        public enum CompareFunction
        {
            Never,
            Less,
            Equal,
            LessOrEqual,
            Greater,
            NotEqual,
            GreaterOrEqual,
            Always
        }
    }
}
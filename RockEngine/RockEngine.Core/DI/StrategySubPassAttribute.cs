namespace RockEngine.Core.DI
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class StrategySubPassAttribute : Attribute
    {
        public Type StrategyType { get; }

        public StrategySubPassAttribute(Type strategyType)
        {
            StrategyType = strategyType;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class RenderPassStrategyAttribute : Attribute
    {
    }
}

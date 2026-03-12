using SimpleInjector.Advanced;

using System.Reflection;

namespace RockEngine.Core.DI
{
    public class GreediestConstructorBehavior : IConstructorResolutionBehavior
    {
        public ConstructorInfo? TryGetConstructor(
            Type implementationType, out string? errorMessage)
        {
            errorMessage = $"{implementationType} has no public constructors.";

            return (
                from ctor in implementationType.GetConstructors()
                orderby ctor.GetParameters().Length ascending
                select ctor)
                .FirstOrDefault();
        }
    }
}

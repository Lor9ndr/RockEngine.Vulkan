using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SimpleInjector.Advanced;

namespace RockEngine.Core.DI
{
    public class GreediestConstructorBehavior : IConstructorResolutionBehavior
    {
        public ConstructorInfo? TryGetConstructor(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType, out string? errorMessage)
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

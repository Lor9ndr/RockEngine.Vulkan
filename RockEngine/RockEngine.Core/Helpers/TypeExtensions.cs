using System.Runtime.CompilerServices;

namespace RockEngine.Core.Helpers
{
    internal static class TypeExtensions
    {
        public static int SizeOf<T>(this T _) where T : allows ref struct
        {
            return Unsafe.SizeOf<T>();
        }
    }
}

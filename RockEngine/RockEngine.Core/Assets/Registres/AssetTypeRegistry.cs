using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RockEngine.Core.Assets.Registres
{
    public class AssetTypeRegistry : IRegistry<Type>
    {
        private readonly ConcurrentDictionary<string, Type> _typeMap = new();
        private readonly ConcurrentDictionary<Type, string> _typeReversedMap = new();


        public void Register<TKey>(TKey typeIdentifier, [NotNull]Type dataType)
        {
            string key = typeIdentifier!.ToString()!;
            _typeMap[key] = dataType;
            _typeReversedMap[dataType] = key;
        }

        public Type Get<TKey>(TKey typeIdentifier)
        {
            return _typeMap[typeIdentifier.ToString()];
        }

        public bool TryGet<TKey>(TKey typeIdentifier, out Type dataType)
        {
            return _typeMap.TryGetValue(typeIdentifier.ToString(), out dataType);
        }

        public bool TryGetTypeIdentifier(Type type, out string identifier)
        {
            return _typeReversedMap.TryGetValue(type, out identifier);
        }
    }

}

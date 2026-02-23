namespace RockEngine.Core.Extensions
{
    public static class Collections
    {
        extension<TKey, TValue>(Dictionary<TKey, TValue> dict) where TKey :notnull
        {
            public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
            {
                if (key is null)
                {
                    throw new KeyNotFoundException();
                }

                ArgumentNullException.ThrowIfNull(valueFactory, nameof(valueFactory));


                if (dict.TryGetValue(key, out var value))
                {
                    return value;
                }
                var result =  valueFactory(key);
                dict.Add(key,result);
                return result;

            }
        }
    }
}

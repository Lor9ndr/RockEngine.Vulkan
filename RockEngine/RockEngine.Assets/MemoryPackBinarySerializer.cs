using MemoryPack;

namespace RockEngine.Assets
{
    public class MemoryPackBinarySerializer : IBinarySerializer
    {
        public MemoryPackBinarySerializer()
        {
        }

        // ---------- бинарная сериализация ----------
        public async Task SerializeAsync<T>(T data, Stream stream)
        {
            await MemoryPackSerializer.SerializeAsync(stream, data).ConfigureAwait(false);
        }

        public async Task SerializeAsync(object data, Type type, Stream stream)
        {
            // MemoryPack требует точный тип; если передан базовый, лучше использовать перегрузку через object
            // Для полиморфных корневых типов используйте MemoryPackSerializer.SerializeAsync<T>.
            // Здесь предполагаем, что data имеет точный runtime‑тип.
            await MemoryPackSerializer.SerializeAsync(type, stream, data).ConfigureAwait(false);
        }

        public async Task<object> DeserializeAsync(Stream stream, Type type)
        {
            // MemoryPack возвращает object?, приводим к object.
            var result = await MemoryPackSerializer.DeserializeAsync(type, stream).ConfigureAwait(false);
            return result!;
        }
    }
}
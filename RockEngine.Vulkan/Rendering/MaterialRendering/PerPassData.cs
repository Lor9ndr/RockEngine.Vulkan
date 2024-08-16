using System.Collections;

namespace RockEngine.Vulkan.Rendering.MaterialRendering
{
    public class PerPassData<T> : IEnumerable<(MeshpassType passType, T value)>
    {
        private static readonly MeshpassType[] _meshpassTypes = Enum.GetValues<MeshpassType>();

        private readonly T[] _data = new T[Enum.GetValues<MeshpassType>().Length];

        public T this[MeshpassType pass]
        {
            get => _data[(int)pass];
            set => _data[(int)pass] = value;
        }

        public IEnumerator<(MeshpassType passType, T value)> GetEnumerator()
        {
            for (int i = 0; i < _meshpassTypes.Length; i++)
            {
                var meshPassType = _meshpassTypes[i];
                yield return (meshPassType, this[meshPassType]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

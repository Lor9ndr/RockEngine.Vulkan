using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;


namespace RockEngine.Benchmarks
{
    public class BenchmarkPinningObjects
    {
        private const int _size = 1000;

        [Benchmark]
        public void AllocateAndAccessWithFixed()
        {
            unsafe
            {
                int[] data = new int[_size];
                fixed (int* ptr = data)
                {
                    for (int i = 0; i < _size; i++)
                    {
                        data[i] = i; // Allocation
                        var value = *(ptr + i); // Access
                    }
                }
            }
        }
        [Benchmark]
        public void AllocateAndAccessWithoutFixed()
        {
            int[] data = new int[_size];
            for (int i = 0; i < _size; i++)
            {
                data[i] = i; // Allocation
                var value = data[i]; // Access
            }
        }

        [Benchmark]
        public void AllocateAndAccessWithMemory()
        {
            Memory<int> memoryData = new Memory<int>(new int[_size]);
            Span<int> span = memoryData.Span;
            for (int i = 0; i < _size; i++)
            {
                span[i] = i; // Allocation
                var value = span[i]; // Access
            }
        }
    }
}

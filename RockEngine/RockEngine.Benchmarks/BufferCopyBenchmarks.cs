using BenchmarkDotNet.Attributes;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace RockEngine.Benchmarks
{
    [MemoryDiagnoser]
    public unsafe class BufferCopyBenchmarks
    {
        [Params(2000, 20000)]
        public int MatrixCount { get; set; }
        private Matrix4x4[] _sourceMatrices;
        private byte[] _matrixDestinationData;

        [GlobalSetup]
        public unsafe void Setup()
        {

            // Initialize matrices
            _sourceMatrices = new Matrix4x4[MatrixCount];
            for (int i = 0; i < MatrixCount; i++)
            {
                _sourceMatrices[i] = Matrix4x4.CreateRotationX(i * 0.1f);
            }
            _matrixDestinationData = new byte[MatrixCount * sizeof(Matrix4x4)];
        }


        //[Benchmark]
        public byte[] CopyMatricesUsingSpan()
        {
            Span<Matrix4x4> source = _sourceMatrices;
            Span<byte> destination = _matrixDestinationData;
            MemoryMarshal.Cast<Matrix4x4, byte>(source).CopyTo(destination);
            return _matrixDestinationData;
        }


        //[Benchmark]
        public byte[] CopyMatricesUsingVector()
        {
            fixed (Matrix4x4* srcPtr = _sourceMatrices)
            fixed (byte* destPtr = _matrixDestinationData)
            {
                int vectorSize = Vector<float>.Count;
                int floatsPerMatrix = 16; // 4x4 matrix
                int totalFloats = MatrixCount * floatsPerMatrix;

                for (int i = 0; i < totalFloats; i += vectorSize)
                {
                    Vector<float> vec = Unsafe.Read<Vector<float>>(&srcPtr[0].M11 + i);
                    Unsafe.Write(destPtr + i * sizeof(float), vec);
                }
            }
            return _matrixDestinationData;
        }

        //[Benchmark]
        public byte[] CopyMatricesUsingGotoBased()
        {
            fixed (Matrix4x4* srcPtr = _sourceMatrices)
            fixed (byte* destPtr = _matrixDestinationData)
            {
                byte* src = (byte*)srcPtr;
                byte* dest = destPtr;
                int remainingBytes = MatrixCount * sizeof(Matrix4x4);

            copy64:
                if (remainingBytes >= 64)
                {
                    *(ulong*)dest = *(ulong*)src;
                    *(ulong*)(dest + 8) = *(ulong*)(src + 8);
                    *(ulong*)(dest + 16) = *(ulong*)(src + 16);
                    *(ulong*)(dest + 24) = *(ulong*)(src + 24);
                    *(ulong*)(dest + 32) = *(ulong*)(src + 32);
                    *(ulong*)(dest + 40) = *(ulong*)(src + 40);
                    *(ulong*)(dest + 48) = *(ulong*)(src + 48);
                    *(ulong*)(dest + 56) = *(ulong*)(src + 56);
                    src += 64;
                    dest += 64;
                    remainingBytes -= 64;
                    goto copy64;
                }

            copy8:
                if (remainingBytes >= 8)
                {
                    *(ulong*)dest = *(ulong*)src;
                    src += 8;
                    dest += 8;
                    remainingBytes -= 8;
                    goto copy8;
                }

                while (remainingBytes > 0)
                {
                    *dest = *src;
                    src++;
                    dest++;
                    remainingBytes--;
                }
            }
            return _matrixDestinationData;
        }

        //[Benchmark]
        public byte[] CopyMatricesUsingRecursion()
        {
            fixed (Matrix4x4* srcPtr = _sourceMatrices)
            fixed (byte* destPtr = _matrixDestinationData)
            {
                RecursiveMatrixCopy(srcPtr, destPtr, MatrixCount);
            }
            return _matrixDestinationData;
        }

        private void RecursiveMatrixCopy(Matrix4x4* src, byte* dest, int count)
        {
            if (count == 1)
            {
                Unsafe.CopyBlock(dest, src, (uint)sizeof(Matrix4x4));
                return;
            }

            int halfCount = count / 2;
            RecursiveMatrixCopy(src, dest, halfCount);
            RecursiveMatrixCopy(src + halfCount, dest + halfCount * sizeof(Matrix4x4), count - halfCount);
        }

        //[Benchmark]
        public byte[] CopyMatricesUsingPointerArithmetic()
        {
            fixed (Matrix4x4* srcPtr = _sourceMatrices)
            fixed (byte* destPtr = _matrixDestinationData)
            {
                float* src = (float*)srcPtr;
                float* dest = (float*)destPtr;
                float* end = src + (MatrixCount * 16); // 16 floats per matrix

                while (src < end)
                {
                    *dest++ = *src++;
                }
            }
            return _matrixDestinationData;
        }

        //[Benchmark]
        public byte[] CopyMatricesUsingUnsafeAs()
        {
            fixed (Matrix4x4* srcPtr = _sourceMatrices)
            fixed (byte* destPtr = _matrixDestinationData)
            {
                for (int i = 0; i < MatrixCount; i++)
                {
                    ref byte srcByte = ref Unsafe.As<Matrix4x4, byte>(ref srcPtr[i]);
                    fixed (byte* pByte = &srcByte)
                    {
                        Unsafe.CopyBlock(destPtr + i * sizeof(Matrix4x4), pByte, (uint)sizeof(Matrix4x4));
                    }

                }
            }
            return _matrixDestinationData;
        }
        //[Benchmark]
        public byte[] CopyMatricesToBuffer()
        {
            Buffer.BlockCopy(_sourceMatrices, 0, _matrixDestinationData, 0, MatrixCount * Unsafe.SizeOf<Matrix4x4>());
            return _matrixDestinationData;
        }

        //[Benchmark]
        public unsafe byte[] CopyMatricesToBufferUnsafe()
        {
            fixed (Matrix4x4* srcPtr = _sourceMatrices)
            fixed (byte* destPtr = _matrixDestinationData)
            {
                Buffer.MemoryCopy(srcPtr, destPtr, _matrixDestinationData.Length, MatrixCount * Unsafe.SizeOf<Matrix4x4>());
            }
            return _matrixDestinationData;
        }

        [Benchmark]
        public unsafe byte[] WriteMatricesToBufferPointer()
        {
            fixed (Matrix4x4* srcPtr = _sourceMatrices)
            fixed (byte* destPtr = _matrixDestinationData)
            {
                WriteToBuffer(srcPtr, destPtr, (ulong)(MatrixCount * Unsafe.SizeOf<Matrix4x4>()));
            }
            return _matrixDestinationData;
        }
        [Benchmark]
        public unsafe byte[] WriteMatricesByAVXorSse()
        {
            fixed (byte* destPtr = _matrixDestinationData)
            {
                CopyMatricesToBufferByAVXOrSSe(_sourceMatrices, destPtr);
                return _matrixDestinationData;
            }

        }

        private unsafe void WriteToBuffer(void* data, void* destination, ulong size)
        {
            Buffer.MemoryCopy(data, destination, size, size);
        }

        private unsafe void WriteToBuffer(nint data, nint destination, ulong size)
        {
            Buffer.MemoryCopy(data.ToPointer(), destination.ToPointer(), size, size);
        }

        public void CopyMatricesToBufferByAVXOrSSe(Matrix4x4[] matrices, void* destination)
        {
            fixed (Matrix4x4* source = matrices)
            {
                int matrixCount = matrices.Length;
                int totalFloats = matrixCount * 16; // 16 floats per matrix
                float* src = (float*)source;
                float* dest = (float*)destination;

                // Use AVX if available
                if (Avx.IsSupported)
                {
                    int vectorSize = Vector256<float>.Count;
                    int vectorCount = totalFloats / vectorSize;

                    for (int i = 0; i < vectorCount; i++)
                    {
                        Vector256<float> vector = Avx.LoadVector256(src + i * vectorSize);
                        Avx.Store(dest + i * vectorSize, vector);
                    }

                    src += vectorCount * vectorSize;
                    dest += vectorCount * vectorSize;
                    totalFloats -= vectorCount * vectorSize;
                }
                // Use SSE if available
                else if (Sse.IsSupported)
                {
                    int vectorSize = Vector128<float>.Count;
                    int vectorCount = totalFloats / vectorSize;

                    for (int i = 0; i < vectorCount; i++)
                    {
                        Vector128<float> vector = Sse.LoadVector128(src + i * vectorSize);
                        Sse.Store(dest + i * vectorSize, vector);
                    }

                    src += vectorCount * vectorSize;
                    dest += vectorCount * vectorSize;
                    totalFloats -= vectorCount * vectorSize;
                }

                // Copy remaining floats
                for (int i = 0; i < totalFloats; i++)
                {
                    *dest++ = *src++;
                }
            }
        }
    }
}
/*

| Method                             | MatrixCount | Mean        | Error      | StdDev     | Median      | Allocated |
|----------------------------------- |------------ |------------:|-----------:|-----------:|------------:|----------:|
| CopyMatricesUsingSpan              | 100         |    44.26 ns |   0.692 ns |   0.647 ns |    44.23 ns |         - | !!!!!!!!!!!
| CopyMatricesUsingVector            | 100         |    73.49 ns |   0.130 ns |   0.102 ns |    73.46 ns |         - |
| CopyMatricesUsingGotoBased         | 100         |   104.40 ns |   0.690 ns |   0.646 ns |   104.53 ns |         - |
| CopyMatricesUsingRecursion         | 100         |   128.69 ns |   1.168 ns |   1.092 ns |   129.01 ns |         - |
| CopyMatricesUsingPointerArithmetic | 100         |   458.30 ns |   4.988 ns |   4.666 ns |   459.69 ns |         - |
| CopyMatricesUsingUnsafeAs          | 100         |    91.22 ns |   0.590 ns |   0.552 ns |    91.21 ns |         - |
| CopyMatricesToBuffer               | 100         |          NA |         NA |         NA |          NA |        NA |
| CopyMatricesToBufferUnsafe         | 100         |    43.31 ns |   0.237 ns |   0.222 ns |    43.21 ns |         - |
| WriteMatricesToBufferPointer       | 100         |    41.52 ns |   0.293 ns |   0.274 ns |    41.53 ns |         - | !!!!!

| CopyMatricesUsingSpan              | 500         |   422.86 ns |   3.905 ns |   3.653 ns |   420.24 ns |         - |
| CopyMatricesUsingVector            | 500         |   404.76 ns |   2.059 ns |   1.926 ns |   403.85 ns |         - | !!!!!!!
| CopyMatricesUsingGotoBased         | 500         |   596.32 ns |   3.557 ns |   3.327 ns |   596.28 ns |         - |
| CopyMatricesUsingRecursion         | 500         |   762.17 ns |   7.550 ns |   7.062 ns |   757.61 ns |         - |
| CopyMatricesUsingPointerArithmetic | 500         | 2,263.41 ns |  29.528 ns |  27.621 ns | 2,258.24 ns |         - |
| CopyMatricesUsingUnsafeAs          | 500         |   483.83 ns |   4.506 ns |   4.215 ns |   483.92 ns |         - |
| CopyMatricesToBuffer               | 500         |          NA |         NA |         NA |          NA |        NA |
| CopyMatricesToBufferUnsafe         | 500         |   420.97 ns |   3.778 ns |   3.534 ns |   419.50 ns |         - |
| WriteMatricesToBufferPointer       | 500         |   420.63 ns |   3.527 ns |   3.299 ns |   417.96 ns |         - |

| CopyMatricesUsingSpan              | 1000        |   838.96 ns |   6.689 ns |   6.257 ns |   835.15 ns |         - |!!
| CopyMatricesUsingVector            | 1000        |   798.96 ns |   4.635 ns |   4.335 ns |   796.87 ns |         - |!!!!!
| CopyMatricesUsingGotoBased         | 1000        | 1,177.14 ns |   7.091 ns |   6.286 ns | 1,173.89 ns |         - |
| CopyMatricesUsingRecursion         | 1000        | 1,513.20 ns |  14.013 ns |  13.107 ns | 1,514.46 ns |         - |
| CopyMatricesUsingPointerArithmetic | 1000        | 4,523.39 ns |  41.947 ns |  39.238 ns | 4,538.11 ns |         - |
| CopyMatricesUsingUnsafeAs          | 1000        |   966.42 ns |   9.975 ns |   9.331 ns |   967.66 ns |         - |
| CopyMatricesToBuffer               | 1000        |          NA |         NA |         NA |          NA |        NA |
| CopyMatricesToBufferUnsafe         | 1000        |   840.61 ns |   8.822 ns |   8.253 ns |   843.41 ns |         - |
| WriteMatricesToBufferPointer       | 1000        |   839.86 ns |   8.508 ns |   7.959 ns |   836.89 ns |         - |

| CopyMatricesUsingSpan              | 2000        | 1,650.82 ns |  12.651 ns |  11.834 ns | 1,643.04 ns |         - |
| CopyMatricesUsingVector            | 2000        | 1,641.06 ns |   8.302 ns |   7.766 ns | 1,636.84 ns |         - |!!
| CopyMatricesUsingGotoBased         | 2000        | 2,343.03 ns |  19.329 ns |  18.081 ns | 2,329.82 ns |         - |
| CopyMatricesUsingRecursion         | 2000        | 2,980.84 ns |  22.217 ns |  20.782 ns | 2,968.10 ns |         - |
| CopyMatricesUsingPointerArithmetic | 2000        | 8,987.68 ns | 178.543 ns | 167.009 ns | 9,053.45 ns |         - |
| CopyMatricesUsingUnsafeAs          | 2000        | 1,938.99 ns |  23.460 ns |  21.945 ns | 1,947.80 ns |         - |
| CopyMatricesToBuffer               | 2000        |          NA |         NA |         NA |          NA |        NA |
| CopyMatricesToBufferUnsafe         | 2000        | 1,652.59 ns |  15.403 ns |  14.408 ns | 1,641.94 ns |         - |
| WriteMatricesToBufferPointer       | 2000        | 1,640.02 ns |   0.563 ns |   0.440 ns | 1,639.95 ns |         - |!!!!
 */
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Buffers;

namespace RockEngine.Core.Assets
{
    public sealed class MeshAsset : Asset<MeshData>, IGpuResource, IMesh, IDisposable
    {
        public override string Type => "Mesh";

        [SerializeIgnore]
        private Vertex[]? Vertices => Data?.Vertices;
        [SerializeIgnore]
        private uint[]? Indices => Data?.Indices;

        [SerializeIgnore]
        public bool GpuReady => _allocation is not null;

        [SerializeIgnore]
        public bool HasIndices => IndicesCount > 0;

        [SerializeIgnore]
        private GlobalGeometryBuffer.MeshAllocation? _allocation;

        [SerializeIgnore]
        public uint? IndicesCount { get; private set; }

        [SerializeIgnore]
        public uint VerticesCount { get; private set; }

        private readonly SemaphoreSlim _gpuLock = new SemaphoreSlim(1, 1);
        private bool _disposed;


        public void SetGeometry(Vertex[] vertices, uint[] indices)
        {
            ArgumentNullException.ThrowIfNull(vertices, nameof(vertices));
            Data ??= new MeshData();

            Data.Vertices = vertices;
            Data.Indices = indices;
            VerticesCount = (uint)Vertices.Length;
            IndicesCount = (uint?)Indices.Length;
        }
        public override void SetData(object data)
        {
            if (data is MeshData meshData)
            {
                Data = meshData;
                SetGeometry(Data.Vertices, Data.Indices);
            }
        }

        public async ValueTask LoadGpuResourcesAsync()
        {
            if (GpuReady) return;

            if (!IsDataLoaded) await LoadDataAsync().ConfigureAwait(true);
            await _gpuLock.WaitAsync().ConfigureAwait(true);
            try
            {
                if (GpuReady) return;

                // Get the global geometry buffer from the context or a service
                var globalGeometryBuffer = IoC.Container.GetInstance<GlobalGeometryBuffer>();

                // Add mesh to global buffer
                _allocation = await globalGeometryBuffer.AddMeshAsync(ID, Vertices!, Indices!);

                IndicesCount = (uint?)Indices?.Length;
                VerticesCount = (uint)Vertices!.Length;
            }
            finally
            {
                _gpuLock.Release();
            }
            UnloadData();
        }

        public override void UnloadData()
        {
            _loadSemaphore.Wait();
            try
            {
                base.UnloadData();
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }

        public void UnloadGpuResources()
        {
            lock (_gpuLock)
            {
                var globalGeometryBuffer = IoC.Container.GetInstance<GlobalGeometryBuffer>();
                globalGeometryBuffer.RemoveMesh(ID);
                _allocation = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            UnloadData();
            UnloadGpuResources();
            _loadSemaphore.Dispose();
            _disposed = true;
        }
    }
}

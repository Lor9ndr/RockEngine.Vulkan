using NLog;

using RockEngine.Core.Assets;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.ResourceProviders;

namespace RockEngine.Core.ECS.Components
{
    public partial class MeshRenderer : Component, IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public MeshProvider _meshProvider
        {
            get => m_meshProvider;
            set => SetProviders(value, _materialProvider);
        }
        public MaterialProvider _materialProvider
        {
            get => m_materialProvider;
            set => SetProviders(m_meshProvider, value);
        }

        [SerializeIgnore]
        public Material Material { get; private set; }

        [SerializeIgnore]
        public IMesh Mesh { get; private set; }

        [SerializeIgnore]
        public bool HasIndices => Mesh?.HasIndices ?? false;

        [SerializeIgnore]
        public uint? IndicesCount => Mesh?.IndicesCount;

        [SerializeIgnore]
        public uint VerticesCount => Mesh?.VerticesCount ?? 0;

        // Track transform index and event handler
        private int _transformIndex = -1;
        private Action<Transform> _transformChangedHandler;
        private bool _isRegistered = false;
        private MeshProvider m_meshProvider;
        private MaterialProvider m_materialProvider;

        public MeshRenderer()
        {
        }

        public void SetProviders(AssetReference<MeshAsset> meshAsset, AssetReference<MaterialAsset> materialAsset)
        {
           
            CleanupExisting();
            m_meshProvider = new MeshProvider(meshAsset);
            m_materialProvider = new MaterialProvider(materialAsset);
            World.GetCurrent().EnqueueForStart(this);
        }

        public void SetProviders(IMesh mesh, Material material)
        {
            CleanupExisting();
            m_meshProvider = new MeshProvider(mesh);
            m_materialProvider = new MaterialProvider(material);
            World.GetCurrent().EnqueueForStart(this);
        }

        public void SetProviders(MeshProvider mesh, MaterialProvider material)
        {
            CleanupExisting();
            m_meshProvider = mesh;
            m_materialProvider = material;
            World.GetCurrent().EnqueueForStart(this);
        }

        public override async ValueTask OnStart(Renderer renderer)
        {
            if (_meshProvider != null && _materialProvider != null && !_isRegistered)
            {
                try
                {
                    Mesh = await _meshProvider.GetAsync();
                    Material = await _materialProvider.GetAsync();
                    renderer.Draw(this);
                    _isRegistered = true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to start mesh renderer");
                    _isRegistered = false;
                }
            }
            else
            {
                _logger.Warn("Attempt to start mesh renderer without asset or material, or already registered");
            }
        }

        private void CleanupExisting()
        {
            if (_isRegistered)
            {
                Dispose();
            }

           /* MeshProvider?.Dispose();
            MaterialProvider?.Dispose();*/

            m_meshProvider = null;
            m_materialProvider = null;
            Material = null;
            Mesh = null;
        }

        internal void SetTransformIndex(int index)
        {
            _transformIndex = index;
        }

        internal void SetTransformChangedHandler(Action<Transform> handler)
        {
            _transformChangedHandler = handler;
        }

        public void Dispose()
        {
            if (_isRegistered)
            {
                var renderer = IoC.Container.GetInstance<Renderer>();
                renderer.StopDrawing(this);
                _isRegistered = false;
            }

            // Clean up event handler
            if (_transformChangedHandler != null && Entity?.Transform != null)
            {
                Entity.Transform.TransformChanged -= _transformChangedHandler;
                _transformChangedHandler = null;
            }

            _transformIndex = -1;

           /* MeshProvider?.Dispose();
            MaterialProvider?.Dispose();*/

            _meshProvider = null;
            _materialProvider = null;
            Material = null;
            Mesh = null;
        }

        ~MeshRenderer()
        {
            Dispose();
        }
    }
}
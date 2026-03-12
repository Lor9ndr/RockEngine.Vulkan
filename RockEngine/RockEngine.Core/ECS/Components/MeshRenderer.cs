using MessagePack;

using NLog;

using RockEngine.Assets;
using RockEngine.Core.Assets;
using RockEngine.Core.Attributes;
using RockEngine.Core.DI;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.ResourceProviders;

namespace RockEngine.Core.ECS.Components
{
    [MessagePackObject]
    public partial class MeshRenderer : Component, IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        [Key(7)]
        public MeshProvider MeshProvider
        {
            get => _meshProvider;
            set => SetProviders(value, MaterialProvider);
        }
        [Key(8)]
        public MaterialProvider MaterialProvider
        {
            get => _materialProvider;
            set => SetProviders(_meshProvider, value);
        }

        [IgnoreMember]
        public Material Material { get; private set; }

        [SerializeIgnore]
        [IgnoreMember]

        public IMesh Mesh { get; private set; }

        [SerializeIgnore]
        [IgnoreMember]

        public bool HasIndices => Mesh?.HasIndices ?? false;

        [SerializeIgnore]
        [IgnoreMember]

        public uint? IndicesCount => Mesh?.IndicesCount;

        [SerializeIgnore]
        [IgnoreMember]

        public uint VerticesCount => Mesh?.VerticesCount ?? 0;

        [Key(14)]
        public bool CastShadows { get; set; } = true;

        // Track transform index and event handler
        [IgnoreMember]

        private int _transformIndex = -1;
        [IgnoreMember]

        private Action<Transform> _transformChangedHandler;
        [IgnoreMember]

        private bool _isRegistered = false;
        [IgnoreMember]

        private MeshProvider _meshProvider;
        [IgnoreMember]

        private MaterialProvider _materialProvider;

        public MeshRenderer()
        {
        }

        public void SetProviders(AssetReference<MeshAsset> meshAsset, AssetReference<MaterialAsset> materialAsset)
        {
           
            CleanupExisting();
            _meshProvider = new MeshProvider(meshAsset);
            _materialProvider = new MaterialProvider(materialAsset);
            World.GetCurrent().EnqueueForStart(this);
        }

        public void SetProviders(IMesh mesh, Material material)
        {
            CleanupExisting();
            _meshProvider = new MeshProvider(mesh);
            _materialProvider = new MaterialProvider(material);
            World.GetCurrent().EnqueueForStart(this);
        }

        public void SetProviders(MeshProvider mesh, MaterialProvider material)
        {
            CleanupExisting();
            _meshProvider = mesh;
            _materialProvider = material;
            World.GetCurrent().EnqueueForStart(this);
        }

        public override async ValueTask OnStart(WorldRenderer renderer)
        {
            if (MeshProvider != null && MaterialProvider != null && !_isRegistered)
            {
                try
                {
                    Mesh = await MeshProvider.GetAsync();
                    Material = await MaterialProvider.GetAsync();
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

            _meshProvider = null;
            _materialProvider = null;
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
                var renderer = IoC.Container.GetInstance<WorldRenderer>();
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

            MeshProvider = null;
            MaterialProvider = null;
            Material = null;
            Mesh = null;
        }

        ~MeshRenderer()
        {
            Dispose();
        }
    }
}
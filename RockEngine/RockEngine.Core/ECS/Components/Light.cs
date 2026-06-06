using System.Numerics;
using System.Runtime.InteropServices;
using MessagePack;
using RockEngine.Core.Attributes;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering;

namespace RockEngine.Core.ECS.Components
{
    public enum LightType
    {
        Directional,
        Point,
        Spot
    }

    [MessagePackObject(AllowPrivate = true)]
    public partial class Light : Component
    {
        [Key(7)]
        public LightType Type
        {
            get;
            set
            {
                field = value;
                DefineShadowMatrixDelegate(field);
            }
        } = LightType.Point;

        private void DefineShadowMatrixDelegate(LightType type)
        {
            switch (type)
            {
                case LightType.Directional:
                    GetShadowMatrix = GetDirectionalShadowMatrices; // Use CSM version
                    break;
                case LightType.Point:
                    GetShadowMatrix = GetPointShadowMatrices;
                    break;
                case LightType.Spot:
                    GetShadowMatrix = UpdateSpotShadowMatrices;
                    break;
            }
        }

        [Color]
        [Key(8)]
        public Vector3 Color { get; set; } = Vector3.One;

        [Range(0, 1000)]
        [Key(9)]
        public float Intensity { get; set; } = 1.0f;


        // Point/Spot properties
        [Range(0.02f, float.MaxValue)]
        [Key(10)]
        public float Radius { get; set; } = 10.0f;

        [IgnoreMember]
        private float _innerCutoff = 0.9f;
        [IgnoreMember]
        private float _outerCutoff = 0.7f;

        [Range(0.1f, 0.99f), Step(0.01f)]
        [Key(13)]
        public float InnerCutoff
        {
            get => _innerCutoff;
            set
            {
                // Validate: Inner cutoff must be greater than outer cutoff
                value = Math.Clamp(value, 0.1f, 0.99f);
                if (value <= _outerCutoff)
                {
                    // Auto-adjust outer cutoff to maintain valid relationship
                    _outerCutoff = Math.Clamp(value - 0.1f, 0.05f, 0.98f);
                }
                _innerCutoff = value;
            }
        }

        [Range(0.05f, 0.98f), Step(0.01f)]
        [Key(14)]
        public float OuterCutoff
        {
            get => _outerCutoff;
            set
            {
                // Validate: Outer cutoff must be less than inner cutoff
                value = Math.Clamp(value, 0.05f, 0.98f);
                if (value >= _innerCutoff)
                {
                    // Auto-adjust inner cutoff to maintain valid relationship
                    _innerCutoff = Math.Clamp(value + 0.1f, 0.1f, 0.99f);
                }
                _outerCutoff = value;
            }
        }

        // Helper properties for degrees (for easier editing)
        [Range(1f, 80f)]
        [Key(15)]
        public float InnerCutoffDegrees
        {
            get => MathHelper.RadiansToDegrees(MathF.Acos(_innerCutoff));
            set => InnerCutoff = MathF.Cos(MathHelper.DegreesToRadians(Math.Clamp(value, 1f, 80f)));
        }

        [Range(5f, 85f)]
        [Key(16)]
        public float OuterCutoffDegrees
        {
            get => MathHelper.RadiansToDegrees(MathF.Acos(_outerCutoff));
            set => OuterCutoff = MathF.Cos(MathHelper.DegreesToRadians(Math.Clamp(value, 5f, 85f)));
        }

        [Key(17)]
        public bool CastShadows { get; set; } = false;

        [Range(0.001f, 0.1f), Step(0.001f)]
        [Key(18)]
        public float ShadowBias { get; set; } = 0.005f;

        [Range(0.0f, 1.0f), Step(0.01f)]
        [Key(19)]
        public float ShadowStrength { get; set; } = 1.0f;

        [Key(20)]
        public uint ShadowMapSize { get; set; } = 1024;

        // Directional light specific shadow properties
        [Key(21)]
        public float ShadowDistance { get; set; } = 100.0f;
        [Key(22)]
        public Vector2 ShadowOrthoSize { get; set; } = new Vector2(200, 200);


        [Range(1, 4)]
        [Key(23)]
        public int CascadeCount { get; set; } = 4;
        [Key(24)]
        public float[] CascadeSplits { get; private set; } = new float[4];

        [Range(0.001f, 0.1f), Step(0.001f)]
        [Key(25)]
        public float CSMShadowBias { get; set; } = 0.001f;

        [Range(0.0f, 0.1f), Step(0.01f)]
        [Key(26)]
        public float NormalOffset { get; set; } = 0.01f;

        [Key(27)]
        public bool StabilizeCascades { get; set; } = true;


        public delegate Matrix4x4[] CalculateShadowMatrixStrategy();

        [IgnoreMember]
        public CalculateShadowMatrixStrategy GetShadowMatrix { get; set; }

        [IgnoreMember]

        private LightData _lightData;
        [IgnoreMember]
        private uint _uboIndex;
        [IgnoreMember]
        private uint _layerStart;

        [IgnoreMember]
        private Matrix4x4[] _cachedDirectionalMatrices = Array.Empty<Matrix4x4>();

        public override ValueTask OnStart(WorldRenderer renderer)
        {
            renderer.LightManager.RegisterLight(this);
            DefineShadowMatrixDelegate(Type);
            return ValueTask.CompletedTask;
        }

        public override ValueTask Update(WorldRenderer renderer)
        {
            if (CastShadows)
            {
                GetShadowMatrix?.Invoke();
            }

            _lightData = new LightData
            {
                PositionAndType = new Vector4(Entity.Transform.WorldPosition, (float)Type),
                DirectionAndRadius = new Vector4(Entity.Transform.Forward, Radius),
                ColorAndIntensity = new Vector4(Color, Intensity),
                Cutoffs = new Vector2(InnerCutoff, OuterCutoff),
                ShadowParams = new Vector4(ShadowBias, ShadowStrength, CastShadows ? 1.0f : 0.0f, _layerStart),
                // For directional we use the externally supplied first cascade matrix;
                // for point/spot the delegate returns the correct single matrix.
                ShadowMatrix = Type == LightType.Directional
                    ? (_cachedDirectionalMatrices.Length > 0 ? _cachedDirectionalMatrices[0] : Matrix4x4.Identity)
                    : GetShadowMatrix is null ? Matrix4x4.Identity : GetShadowMatrix()[0],
            };

            return ValueTask.CompletedTask;
        }



        public ref LightData GetLightData()
        {
            return ref _lightData;
        }

        public void SetShadowIndices(uint layerStart)
        {
            //_uboIndex = uboIndex;
            _layerStart = layerStart;
            // Also update the GPU-side LightData struct if you have one
            ref var data = ref GetLightData(); // assuming you have a method to get the struct
            data.ShadowParams = new Vector4(ShadowBias, ShadowStrength, CastShadows ? 1f : 0f, layerStart);
        }

        private Matrix4x4[] UpdateSpotShadowMatrices()
        {
            var lightPos = Entity.Transform.WorldPosition;
            var lightDir = Vector3.Normalize(Entity.Transform.EulerAngles);

            // OuterCutoff is cosine of half angle, convert to full FOV angle
            float halfAngle = MathF.Acos(Math.Clamp(OuterCutoff, 0.001f, 0.999f));
            float fov = 2.0f * halfAngle; // Full FOV angle in radians

            Vector3 up = Vector3.UnitY;
            if (Math.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.99f)
            {
                up = Vector3.UnitZ;
            }

            var target = lightPos + lightDir;

            var view = Matrix4x4.CreateLookAt(
                lightPos,
                target,
                up);

            var projection = Matrix4x4.CreatePerspectiveFieldOfView(
                fov,
                1.0f, // Aspect ratio (square shadow map)
                0.1f,
                ShadowDistance);

            projection.M22 *= -1;

            // Flip Y-axis for Vulkan viewport
            /* if (Matrix4x4.Invert(projection, out var invProj))
             {
                 var vulkanProjection = projection;
                 vulkanProjection.M22 *= -1; // Flip Y axis for Vulkan
                 projection = vulkanProjection;
             }*/

            return [view * projection];
        }

        // In your Light component class
        private Matrix4x4[] GetPointShadowMatrices()
        {
            if (Type != LightType.Point)
            {
                return [Matrix4x4.Identity];
            }

            var matrices = new Matrix4x4[6];
            var position = Entity.Transform.WorldPosition;
            var far = Radius;

            // Calculate the 6 view-projection matrices for cube map faces
            // +X, -X, +Y, -Y, +Z, -Z
            var projections = new[]
             {
                Matrix4x4.CreateLookAt(position, position + Vector3.UnitX, -Vector3.UnitY),   // +X
                Matrix4x4.CreateLookAt(position, position - Vector3.UnitX, -Vector3.UnitY),   // -X
                Matrix4x4.CreateLookAt(position, position + Vector3.UnitY, Vector3.UnitZ),    // +Y
                Matrix4x4.CreateLookAt(position, position - Vector3.UnitY, -Vector3.UnitZ),   // -Y
                Matrix4x4.CreateLookAt(position, position + Vector3.UnitZ, -Vector3.UnitY),   // +Z
                Matrix4x4.CreateLookAt(position, position - Vector3.UnitZ, -Vector3.UnitY)    // -Z
            };

            var projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 2.0f,  // 90 degree FOV for each face
                1.0f,             // Aspect ratio 1:1
                0.1f,             // Near plane
                far               // Far plane
            );

            for (int i = 0; i < 6; i++)
            {
                matrices[i] = projections[i] * projectionMatrix;
            }

            return matrices;
        }
       
        public void UpdateCascadeSplits(float cameraFarPlane)
        {
            float near = 0.1f;
            float far = Math.Min(cameraFarPlane, ShadowDistance);
            float range = far - near;
            float ratio = far / near;

            for (int i = 0; i < CascadeCount; i++)
            {
                float p = (i + 1) / (float)CascadeCount;
                float log = near * MathF.Pow(ratio, p);
                float uniform = near + range * p;

                // Use more uniform distribution for directional lights
                CascadeSplits[i] = 0.7f * uniform + 0.2f * log;
            }
        }

        /// <summary>Sets the pre‑computed directional shadow matrices from the Camera/ShadowManager.</summary>
        public void SetDirectionalShadowMatrices(Matrix4x4[] matrices)
        {
            _cachedDirectionalMatrices = matrices;
        }

        private Matrix4x4[] GetDirectionalShadowMatrices()
        {
            return _cachedDirectionalMatrices;
        }


    }

    [GLSLStruct(GLSLMemoryLayout.Scalar)]
    public struct LightData
    {
        public Vector4 PositionAndType;       // 16 bytes
        public Vector4 DirectionAndRadius;    // 16 bytes  
        public Vector4 ColorAndIntensity;     // 16 bytes
        public Vector4 ShadowParams;          // 16 bytes
        public Matrix4x4 ShadowMatrix;        // 64 bytes
        public Vector2 Cutoffs;               // 8 bytes

        public static ulong DataSize { get; } = (ulong)Marshal.SizeOf<LightData>();
    }
}
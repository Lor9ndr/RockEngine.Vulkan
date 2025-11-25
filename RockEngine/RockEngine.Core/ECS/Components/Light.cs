using RockEngine.Core.Attributes;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Core.ECS.Components
{
    public enum LightType
    {
        Directional,
        Point,
        Spot
    }

    public partial class Light : Component
    {
        public LightType Type
        {
            get;
            set
            {
                field = value;

                switch (field)
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
        } = LightType.Point;

        [Color]
        public Vector3 Color { get; set; } = Vector3.One;

        [Range(0,1000)]
        public float Intensity { get; set; } = 1.0f;

        // Directional/Spot properties
        public Vector3 Direction { get; set; } = Vector3.UnitY;

        // Point/Spot properties
        [Range(0.02f, float.MaxValue)]
        public float Radius { get; set; } = 10.0f;

        private float _innerCutoff = 0.9f;
        private float _outerCutoff = 0.7f;

        [Range(0.1f, 0.99f), Step(0.01f)]
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
        public float InnerCutoffDegrees
        {
            get => MathHelper.RadiansToDegrees(MathF.Acos(_innerCutoff));
            set => InnerCutoff = MathF.Cos(MathHelper.DegreesToRadians(Math.Clamp(value, 1f, 80f)));
        }

        [Range(5f, 85f)]
        public float OuterCutoffDegrees
        {
            get => MathHelper.RadiansToDegrees(MathF.Acos(_outerCutoff));
            set => OuterCutoff = MathF.Cos(MathHelper.DegreesToRadians(Math.Clamp(value, 5f, 85f)));
        }

        public bool CastShadows { get; set; } = true;

        [Range(0.001f, 0.1f), Step(0.001f)]
        public float ShadowBias { get; set; } = 0.005f;

        [Range(0.0f, 1.0f), Step(0.01f)]
        public float ShadowStrength { get; set; } = 1.0f;

        public uint ShadowMapSize { get; set; } = 1024;

        // Directional light specific shadow properties
        public float ShadowDistance { get; set; } = 100.0f;
        public Vector2 ShadowOrthoSize { get; set; } = new Vector2(200, 200);

        [Range(0.1f, 100)]
        public float ShadowNearPlane { get => field; set => field = Math.Clamp(value, 0.1f, ShadowFarPlane ); } = 0.1f;

        [Range(1.1f, 1000.0f)]
        public float ShadowFarPlane
        {
            get => field;
            set => field = Math.Clamp(value, ShadowNearPlane + 0.1f, 1000);
        } = 1000.0f;  // Should match or be less than Radius


        [Range(1,4)]
        public int CascadeCount { get; set; } = 4;
        public float[] CascadeSplits { get; private set; } = new float[4];

        [Range(0.001f, 0.1f), Step(0.001f)]
        public float CSMShadowBias { get; set; } = 0.001f;

        [Range(0.0f, 0.1f), Step(0.01f)]
        public float NormalOffset { get; set; } = 0.01f;

        public bool StabilizeCascades { get; set; } = true;

        public delegate Matrix4x4[] CalculateShadowMatrixStrategy();
        public CalculateShadowMatrixStrategy GetShadowMatrix { get; set; } 

        private LightData _lightData;
        private int _shadowMapIndex = -1;


        public override ValueTask OnStart(WorldRenderer renderer)
        {
            renderer.LightManager.RegisterLight(this);
            return ValueTask.CompletedTask;
        }

        public override ValueTask Update(WorldRenderer renderer)
        {
            if (CastShadows)
            {
                GetShadowMatrix.Invoke();
            }
            _lightData = new LightData
            {
                PositionAndType = new Vector4(Entity.Transform.WorldPosition, (float)Type),
                DirectionAndRadius = new Vector4(Direction, Radius),
                ColorAndIntensity = new Vector4(Color, Intensity),
                Cutoffs = new Vector2(InnerCutoff, OuterCutoff),
                ShadowParams = new Vector4(ShadowBias, ShadowStrength, CastShadows ? 1.0f : 0.0f, _shadowMapIndex),
                ShadowMatrix = GetShadowMatrix()[0],
            };

            return ValueTask.CompletedTask;
        }

        

        public ref LightData GetLightData()
        {
            return ref _lightData;
        }

   

        internal void SetShadowMapIndex(uint index)
        {
            _shadowMapIndex = (int)index;
            _lightData.ShadowParams = new Vector4(ShadowBias, ShadowStrength, CastShadows ? 1.0f : 0.0f, index);
        }

        private Matrix4x4[] UpdateSpotShadowMatrices()
        {
            var lightPos = Entity.Transform.WorldPosition;
            var lightDir = Vector3.Normalize(Direction);

            // OuterCutoff is cosine of half angle, convert to full FOV angle
            float halfAngle = MathF.Acos(Math.Clamp(OuterCutoff, 0.001f, 0.999f));
            float fov = 2.0f * halfAngle; // Full FOV angle in radians

            Vector3 up = Vector3.UnitY;
            if (Math.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.99f)
                up = Vector3.UnitZ;

            var target = lightPos + lightDir;

            var view = Matrix4x4.CreateLookAt(
                lightPos,
                target,
                up);

            var projection = Matrix4x4.CreatePerspectiveFieldOfView(
                fov,
                1.0f, // Aspect ratio (square shadow map)
                ShadowNearPlane,
                ShadowFarPlane);

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
                return [Matrix4x4.Identity];

            var matrices = new Matrix4x4[6];
            var position = Entity.Transform.Position;
            var far = ShadowFarPlane;

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
        public Matrix4x4[] CalculateCSMMatrices(Camera camera, Vector3 lightDirection)
        {
            var matrices = new Matrix4x4[CascadeCount];

            // Update cascade splits based on current camera
            UpdateCascadeSplits(camera.FarClip);

            float cameraNear = camera.NearClip;

            for (int i = 0; i < CascadeCount; i++)
            {
                float cascadeNear = (i == 0) ? cameraNear : CascadeSplits[i - 1];
                float cascadeFar = CascadeSplits[i];

                matrices[i] = GetDirectionalLightSpaceMatrix(camera, lightDirection, cascadeNear, cascadeFar);
            }

            return matrices;
        }

        private Matrix4x4 GetDirectionalLightSpaceMatrix(Camera camera, Vector3 lightDirection, float nearPlane, float farPlane)
        {
            // Get camera frustum corners for this cascade
            var corners = GetFrustumCornersWorldSpace(camera, nearPlane, farPlane);

            // Calculate the bounding sphere of the frustum
            Vector3 frustumCenter = CalculateFrustumCenter(corners);
            float frustumRadius = CalculateFrustumRadius(corners, frustumCenter);

            Vector3 up = Math.Abs(Vector3.Dot(lightDirection, Vector3.UnitY)) > 0.99f
                ? Vector3.UnitZ
                : Vector3.UnitY;

            var lightView = CreateStabilizedLightView(frustumCenter, lightDirection, up, frustumRadius);

            // Calculate bounds in light space
            CalculateFrustumBoundsInLightSpace(corners, lightView,
                out float minX, out float maxX,
                out float minY, out float maxY,
                out _, out _);

            float worldUnitsPerTexel = (maxX - minX) / ShadowMapSize;
            float padding = worldUnitsPerTexel * 2.0f; // 2 texels padding

            minX -= padding; maxX += padding;
            minY -= padding; maxY += padding;

            float minZ = -frustumRadius * 3.0f;
            float maxZ = frustumRadius * 3.0f;

            var lightProjection = Matrix4x4.CreateOrthographicOffCenter(minX, maxX, minY, maxY, minZ, maxZ);

            return lightView * lightProjection;
        }
        private Matrix4x4 CreateStabilizedLightView(Vector3 frustumCenter, Vector3 lightDirection, Vector3 up, float frustumRadius)
        {
            if (StabilizeCascades)
            {
                float worldUnitsPerTexel = (frustumRadius * 2.0f) / ShadowMapSize;

                // Snap the frustum center to the nearest texel in light space
                Vector3 lightSpaceCenter = Vector3.Transform(frustumCenter,
                    Matrix4x4.CreateLookAt(Vector3.Zero, lightDirection, up));

                lightSpaceCenter.X = MathF.Floor(lightSpaceCenter.X / worldUnitsPerTexel) * worldUnitsPerTexel;
                lightSpaceCenter.Y = MathF.Floor(lightSpaceCenter.Y / worldUnitsPerTexel) * worldUnitsPerTexel;
                lightSpaceCenter.Z = 0; // Don't snap in Z direction

                // Transform back to world space
                if (Matrix4x4.Invert(Matrix4x4.CreateLookAt(Vector3.Zero, lightDirection, up), out Matrix4x4 inverseLightView))
                {
                    frustumCenter = Vector3.Transform(lightSpaceCenter, inverseLightView);
                }
            }

            // Position light far enough back to see entire frustum
            Vector3 lightPosition = frustumCenter - lightDirection * (frustumRadius * 2.0f);

            return Matrix4x4.CreateLookAt(lightPosition, frustumCenter, up);
        }

        private Vector3 CalculateFrustumCenter(Vector3[] corners)
        {
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);

            foreach (var corner in corners)
            {
                min = Vector3.Min(min, corner);
                max = Vector3.Max(max, corner);
            }

            return (min + max) * 0.5f;
        }

        private float CalculateFrustumRadius(Vector3[] corners, Vector3 center)
        {
            float maxDistance = 0;
            foreach (var corner in corners)
            {
                float distance = Vector3.Distance(corner, center);
                maxDistance = Math.Max(maxDistance, distance);
            }
            return maxDistance;
        }

        private void CalculateFrustumBoundsInLightSpace(Vector3[] corners, Matrix4x4 lightView,
            out float minX, out float maxX, out float minY, out float maxY, out float minZ, out float maxZ)
        {
            minX = float.MaxValue; maxX = float.MinValue;
            minY = float.MaxValue; maxY = float.MinValue;
            minZ = float.MaxValue; maxZ = float.MinValue;

            foreach (var corner in corners)
            {
                var lightSpacePos = Vector4.Transform(new Vector4(corner, 1.0f), lightView);
                minX = Math.Min(minX, lightSpacePos.X);
                maxX = Math.Max(maxX, lightSpacePos.X);
                minY = Math.Min(minY, lightSpacePos.Y);
                maxY = Math.Max(maxY, lightSpacePos.Y);
                minZ = Math.Min(minZ, lightSpacePos.Z);
                maxZ = Math.Max(maxZ, lightSpacePos.Z);
            }
        }
        private Vector3[] GetFrustumCornersWorldSpace(Camera camera, float nearPlane, float farPlane)
        {
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(camera.Fov),
                camera.AspectRatio,
                nearPlane,
                farPlane);

            // Combine with camera view matrix
            var viewProjection = camera.ViewMatrix * projection;

            // Invert to get from clip space to world space
            if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse))
            {
                inverse = Matrix4x4.Identity;
            }

            var corners = new Vector3[8];
            int index = 0;

            // Generate all 8 corners of the frustum
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        // Clip space coordinates (-1 to 1)
                        Vector4 clipSpacePos = new Vector4(
                            x * 2.0f - 1.0f,
                            y * 2.0f - 1.0f,
                            z * 2.0f - 1.0f,
                            1.0f);

                        // Transform to world space
                        Vector4 worldSpacePos = Vector4.Transform(clipSpacePos, inverse);

                        // Perspective divide
                        if (Math.Abs(worldSpacePos.W) > float.Epsilon)
                            worldSpacePos /= worldSpacePos.W;

                        corners[index++] = new Vector3(worldSpacePos.X, worldSpacePos.Y, worldSpacePos.Z);
                    }
                }
            }

            return corners;
        }

        public void UpdateCascadeSplits(float cameraFarPlane)
        {
            float near = 0.1f;
            float far = Math.Min(cameraFarPlane, ShadowFarPlane);
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

        private Matrix4x4[] GetDirectionalShadowMatrices()
        {
            // This will be updated by ShadowManager with proper camera context
            var matrices = new Matrix4x4[CascadeCount];
            for (int i = 0; i < CascadeCount; i++)
            {
                matrices[i] = Matrix4x4.Identity;
            }
            return matrices;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LightData
    {
        public static ulong DataSize { get; } = (ulong)Marshal.SizeOf<LightData>();

        public Vector4 PositionAndType;       // 16 bytes
        public Vector4 DirectionAndRadius;    // 16 bytes  
        public Vector4 ColorAndIntensity;     // 16 bytes
        public Vector2 Cutoffs;               // 8 bytes
        public Vector2 _padding;              // 8 bytes explicit padding
        public Vector4 ShadowParams;          // 16 bytes
        public Matrix4x4 ShadowMatrix;        // 64 bytes
    }
}
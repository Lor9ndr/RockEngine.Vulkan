using System.Numerics;
using MemoryPack;
using RockEngine.Core.Attributes;
using RockEngine.Core.Helpers;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Core.Rendering.RenderTargets;
using RockEngine.Core.Rendering.ResourceBindings;
using static RockEngine.Core.Rendering.Managers.CameraManager;

namespace RockEngine.Core.ECS.Components
{
    [MemoryPackable]
    public partial class Camera : Component
    {
        public const int MAX_FOV = 120;
        public const int MIN_FOV = 30;
        private float _aspectRatio;

        private float _nearClip;

        private float _farClip;

        private Matrix4x4 _viewMatrix;

        private Matrix4x4 _projectionMatrix;

        private Matrix4x4 _viewProjectionMatrix;

        private float _fov = MathHelper.PiOver2;

        // Rotation around the X axis (radians)

        private float _pitch = -MathHelper.PiOver2;

        private float _yaw = -MathHelper.PiOver2; // Without this you would be started rotated 90 degrees right


        private Vector3 _right = Vector3.UnitX;

        private Vector3 _up;

        private Vector3 _front;

        private InputAttachmentBinding _attachmentBinding;


        public Vector3 Right => _right;


        public Vector3 Up => _up;


        public Matrix4x4 ViewProjectionMatrix => _viewProjectionMatrix;

        public virtual RenderLayerMask VisibleLayers { get; set; } = RenderLayerMask.All & ~RenderLayerMask.Debug;

        [Step(1)]
        public float Fov
        {
            get => MathHelper.RadiansToDegrees(_fov);
            set
            {
                var angle = Math.Clamp(value, MIN_FOV, MAX_FOV);
                _fov = MathHelper.DegreesToRadians(angle);
            }
        }

        public float AspectRatio
        {
            get => _aspectRatio;
            set
            {
                _aspectRatio = value;
            }
        }

        [Range(0.001f, float.MaxValue)]
        [Step(0.001f)]
        public float NearClip
        {
            get => _nearClip;
            set
            {
                _nearClip = value;
            }
        }

        [Range(0.001f, float.MaxValue)]
        [Step(0.001f)]
        public float FarClip
        {
            get => _farClip;
            set
            {
                _farClip = value;
            }
        }

        public Vector3 Forward
        {
            get => _front;
            set
            {
                _front = value;
            }
        }

        public float Pitch
        {
            get => MathHelper.RadiansToDegrees(_pitch);
            set
            {
                // We clamp the pitch value between -89 and 89 to prevent the camera from going upside down, and a bunch
                // of weird "bugs" when you are using euler angles for rotation.
                // If you want to read more about this you can try researching a topic called gimbal lock
                var angle = Math.Clamp(value, -89f, 89f);
                _pitch = MathHelper.DegreesToRadians(angle);
            }
        }

        public float Yaw
        {
            get => MathHelper.RadiansToDegrees(_yaw);
            set
            {
                _yaw = MathHelper.DegreesToRadians(value);
            }
        }

        [Step(0.001f), Range(0.1f, 4.0f)]
        public float Exposure { get; set; } = 1.0f;

        [Step(0.001f), Range(0.0f, 2.0f)]
        public float EnvIntensity { get; set; } = 1.0f;

        [Step(0.001f), Range(0.0f, 2.0f)]
        public float AoStrength { get; set; } = 1.0f;

        [Step(0.01f), Range(1.8f, 2.4f)]
        public float Gamma { get; set; } = 2.2f;

        [Step(0.01f), Range(0.0f, (float)(2 * Math.PI))]
        public float EnvRotation { get; set; } = 0.0f;

        [MemoryPackIgnore]
        public RenderTarget RenderTarget { get; set; }
        public Matrix4x4 ViewMatrix { get => _viewMatrix; set => _viewMatrix = value; }
        public Matrix4x4 ProjectionMatrix { get => _projectionMatrix; set => _projectionMatrix = value; }

        public Camera()
        {
            _fov = MathHelper.DegreesToRadians(90);
            _aspectRatio = 16 / 9; // just for now, we have to change it by window
            _nearClip = 0.1f;
            _farClip = 1000;
        }
        public void UpdateViewMatrix()
        {
            _viewMatrix = Matrix4x4.CreateLookAt(Entity.Transform.WorldPosition, Entity.Transform.WorldPosition + Forward, _up);
            UpdateProjectionMatrix();
        }

        public void UpdateProjectionMatrix()
        {
            _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(_fov, _aspectRatio, _nearClip, _farClip);
            // flipside the perspective because vulkan(or System.Numerics) idk
            _projectionMatrix.M22 *= -1;

            UpdateViewProjectionMatrix();
        }

        public void UpdateViewProjectionMatrix()
        {
            _viewProjectionMatrix = _viewMatrix * _projectionMatrix;
        }

        public void UpdateVectors()
        {
            // First the front matrix is calculated using some basic trigonometry
            _front = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Sin(_yaw));

            // We need to make sure the vectors are all normalized, as otherwise we would get some funky results
            _front = Vector3.Normalize(_front);

            // Calculate both the right and the up vector using cross product
            // Note that we are calculating the right from the global up, this behaviour might
            // not be what you need for all cameras so keep this in mind if you do not want a FPS camera
            _right = Vector3.Normalize(Vector3.Cross(_front, Vector3.UnitY));
            _up = Vector3.Normalize(Vector3.Cross(_right, _front));
            UpdateViewMatrix();
        }

        public override ValueTask OnStart(WorldRenderer renderer)
        {
            var camIndex = renderer.RegisterCamera(this);
            if (RenderTarget is null)
            {
                RenderTarget = new CameraRenderTarget(renderer.Context, renderer.GraphicsEngine, new Silk.NET.Vulkan.Extent2D(1280, 720));
                RenderTarget.Initialize(renderer.RenderPass ?? throw new Exception("Renderer Renderpass was not created"));
                InitializeGBuffer(((CameraRenderTarget)RenderTarget).GBuffer, renderer, camIndex);
            }
            return default;
        }
        private void InitializeGBuffer(GBuffer gbuffer, WorldRenderer renderer, int cameraIndex)
        {

            var pipeline = renderer.PipelineManager.GetPipelineByName("DeferredLighting");
            RenderTarget.Material = new Material("GBuffer");
            var material = RenderTarget.Material;
            material.AddPass(LightingPass.Name, new MaterialPass(pipeline));

            _attachmentBinding = new InputAttachmentBinding(
                setLocation: 2,
                bindingLocation: 0,
                [.. gbuffer.ColorAttachments.Concat([gbuffer.DepthAttachment])]  // Position + Normal + Albedo
            );
            material.BindResource(_attachmentBinding);
            material.BindResource(renderer.GlobalUbo.GetBinding((uint)cameraIndex));

            material.BindResource(new UniformBufferBinding(renderer.LightManager.CountLightUbo, 1, 1));
            material.SetPushConstant("iblParams", new IBLParams()
            {
                Exposure = Exposure,
                EnvIntensity = EnvIntensity,
                AoStrength = AoStrength,
                Gamma = Gamma,
                EnvRotation = EnvRotation
            });

        }


        public override ValueTask Update(WorldRenderer renderer)
        {
            UpdateVectors();
            RenderTarget?.Material?.SetPushConstant("iblParams", new IBLParams()
            {
                Exposure = Exposure,
                EnvIntensity = EnvIntensity,
                AoStrength = AoStrength,
                Gamma = Gamma,
                EnvRotation = EnvRotation
            });
            return default;
        }

        public virtual bool CanRender(Entity entity)
        {
            return VisibleLayers.Contains(entity.Layer);
        }

        public override void SetActive(bool isActive = true)
        {
            if (isActive == IsActive)
            {
                return;
            }
            IsActive = isActive;
        }

        public Vector3[] GetFrustumCornersWorldSpace(float nearPlane, float farPlane)
        {
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(Fov),
                AspectRatio,
                nearPlane,
                farPlane);

            // Combine with camera view matrix
            var viewProjection = ViewMatrix * projection;

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
                        {
                            worldSpacePos /= worldSpacePos.W;
                        }

                        corners[index++] = new Vector3(worldSpacePos.X, worldSpacePos.Y, worldSpacePos.Z);
                    }
                }
            }

            return corners;
        }

        /// <summary>
        /// Calculates logarithmic/uniform cascade splits based on the camera's clip planes.
        /// </summary>
        public float[] ComputeCascadeSplits(float maxShadowDistance, int cascadeCount)
        {
            float near = NearClip;
            float far = Math.Min(FarClip, maxShadowDistance);
            float range = far - near;
            float ratio = far / near;
            float[] splits = new float[cascadeCount];

            for (int i = 0; i < cascadeCount; i++)
            {
                float p = (i + 1) / (float)cascadeCount;
                float log = near * MathF.Pow(ratio, p);
                float uniform = near + range * p;
                splits[i] = 0.7f * uniform + 0.2f * log;
            }
            return splits;
        }

        /// <summary>
        /// Builds CSM matrices for a directional light using this camera's view frustum.
        /// </summary>
        public Matrix4x4[] ComputeCSMMatrices(Light light)
        {
            int count = light.CascadeCount;
            var splits = ComputeCascadeSplits(light.ShadowDistance, count);
            var matrices = new Matrix4x4[count];

            float cameraNear = NearClip;
            for (int i = 0; i < count; i++)
            {
                float cascadeNear = (i == 0) ? cameraNear : splits[i - 1];
                float cascadeFar = splits[i];
                matrices[i] = CalculateDirectionalLightSpaceMatrix(light, cascadeNear, cascadeFar);
            }
            return matrices;
        }

        private Matrix4x4 CalculateDirectionalLightSpaceMatrix(Light light, float nearPlane, float farPlane)
        {
            var corners = GetFrustumCornersWorldSpace(nearPlane, farPlane);
            Vector3 frustumCenter = CalculateFrustumCenter(corners);
            float frustumRadius = CalculateFrustumRadius(corners, frustumCenter);

            Vector3 up = Math.Abs(Vector3.Dot(light.Entity.Transform.Forward, Vector3.UnitY)) > 0.99f
                ? Vector3.UnitZ : Vector3.UnitY;

            var lightView = CreateStabilizedLightView(light, frustumCenter, frustumRadius, up);

            CalculateFrustumBoundsInLightSpace(corners, lightView,
                out float minX, out float maxX, out float minY, out float maxY, out _, out _);

            float worldUnitsPerTexel = (maxX - minX) / light.ShadowMapSize;
            float padding = worldUnitsPerTexel * 2.0f;

            minX -= padding;
            maxX += padding;
            minY -= padding;
            maxY += padding;
            float minZ = -frustumRadius * 3.0f;
            float maxZ = frustumRadius * 3.0f;

            var lightProjection = Matrix4x4.CreateOrthographicOffCenter(minX, maxX, minY, maxY, minZ, maxZ);
            return lightView * lightProjection;
        }

        private static Matrix4x4 CreateStabilizedLightView(Light light, Vector3 frustumCenter, float frustumRadius, Vector3 up)
        {
            Vector3 lightDir = light.Entity.Transform.Forward;
            if (light.StabilizeCascades)
            {
                float worldUnitsPerTexel = (frustumRadius * 2.0f) / light.ShadowMapSize;
                Vector3 lightSpaceCenter = Vector3.Transform(frustumCenter,
                    Matrix4x4.CreateLookAt(Vector3.Zero, lightDir, up));
                lightSpaceCenter.X = MathF.Floor(lightSpaceCenter.X / worldUnitsPerTexel) * worldUnitsPerTexel;
                lightSpaceCenter.Y = MathF.Floor(lightSpaceCenter.Y / worldUnitsPerTexel) * worldUnitsPerTexel;
                lightSpaceCenter.Z = 0;

                if (Matrix4x4.Invert(Matrix4x4.CreateLookAt(Vector3.Zero, lightDir, up), out var inv))
                {
                    frustumCenter = Vector3.Transform(lightSpaceCenter, inv);
                }
            }

            Vector3 lightPos = frustumCenter - lightDir * (frustumRadius * 2.0f);
            return Matrix4x4.CreateLookAt(lightPos, frustumCenter, up);
        }
        private static Vector3 CalculateFrustumCenter(Vector3[] corners)
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

        private static float CalculateFrustumRadius(Vector3[] corners, Vector3 center)
        {
            float maxDistance = 0;
            foreach (var corner in corners)
            {
                float distance = Vector3.Distance(corner, center);
                maxDistance = Math.Max(maxDistance, distance);
            }
            return maxDistance;
        }

        private static void CalculateFrustumBoundsInLightSpace(Vector3[] corners, Matrix4x4 lightView,
            out float minX, out float maxX, out float minY, out float maxY, out float minZ, out float maxZ)
        {
            minX = float.MaxValue;
            maxX = float.MinValue;
            minY = float.MaxValue;
            maxY = float.MinValue;
            minZ = float.MaxValue;
            maxZ = float.MinValue;

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
    }
}

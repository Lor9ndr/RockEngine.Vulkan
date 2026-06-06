using System.Numerics;
using System.Runtime.InteropServices;
using RockEngine.Core;
using RockEngine.Core.Assets;
using RockEngine.Core.Builders;
using RockEngine.Core.CoreObjects;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Helpers;
using RockEngine.Core.Physics;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Managers;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Core.ResourceProviders;
using RockEngine.Editor.Rendering.Passes;
using RockEngine.Editor.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;
using Silk.NET.Vulkan;

namespace RockEngine.Editor.EditorComponents
{
    /// <summary>
    /// Defines the current operation mode of the transform gizmo.
    /// </summary>
    public enum GizmoType : uint
    {
        /// <summary>Translation mode (move).</summary>
        Translate = 0,
        /// <summary>Rotation mode.</summary>
        Rotate = 1,
        /// <summary>Scale mode.</summary>
        Scale = 2
    }

    /// <summary>
    /// Flags identifying which part of the gizmo is selected or hovered.
    /// </summary>
    [Flags]
    public enum GizmoAxis : uint
    {
        /// <summary>No axis is selected.</summary>
        None = 0,
        /// <summary>X axis (red).</summary>
        X = 1,
        /// <summary>Y axis (green).</summary>
        Y = 2,
        /// <summary>Z axis (blue).</summary>
        Z = 4,
        /// <summary>Uniform operation (center cube/sphere).</summary>
        Uniform = 8,
        /// <summary>View‑aligned operation (e.g., view plane).</summary>
        View = 16
    }

    /// <summary>
    /// Push constants for the gizmo vertex shader.
    /// </summary>
    [GLSLStruct]
    public struct GizmoPushConstants
    {
        /// <summary>Base color of the gizmo.</summary>
        public Vector4 GizmoColor;
        /// <summary>Current gizmo type (translate/rotate/scale).</summary>
        public uint GizmoType;
        private float _padding1;
        private float _padding2;
        private float _padding3;
    }

    /// <summary>
    /// Push constants for the gizmo fragment shader (used for picking).
    /// </summary>
    [GLSLStruct]
    public struct GizmoPushFragConstants
    {
        /// <summary>Current gizmo type.</summary>
        public uint GizmoType;
        /// <summary>Mask of the selected axis.</summary>
        public uint AxisMask;
        private float _padding1;
        private float _padding2;
    }

    /// <summary>
    /// An editor‑only component that renders a transform gizmo (translate, rotate, scale)
    /// and handles interactive manipulation of the attached entity.
    /// </summary>
    public partial class TransformGizmo : Component
    {
        private GizmoType _currentMode = GizmoType.Translate;
        private GizmoAxis _selectedAxis = GizmoAxis.None;
        private GizmoAxis _hoveredAxis = GizmoAxis.None;
        private bool _isDragging = false;
        private Vector2 _dragStartPosition;
        private Vector3 _dragStartWorldPos;
        private Quaternion _dragStartRotation;
        private Vector3 _dragStartScale;

        private Material _gizmoMaterial;
        private MeshRenderer? _meshRenderer;
        private Vector2 _viewportSize;
        private Vector2 _currentImageMin;
        private Vector2 _currentImageMax;

        // Drag plane data
        private Vector3 _dragPlanePoint;
        private Vector3 _dragPlaneNormal;
        private Vector3 _dragAxisDirection;

        // Colors for different axes
        private readonly Vector4 _colorX = new Vector4(0.9f, 0.2f, 0.2f, 1.0f);
        private readonly Vector4 _colorY = new Vector4(0.2f, 0.9f, 0.2f, 1.0f);
        private readonly Vector4 _colorZ = new Vector4(0.2f, 0.4f, 1.0f, 1.0f);
        private readonly Vector4 _colorUniform = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
        private readonly Vector4 _colorHover = new Vector4(1.0f, 0.9f, 0.2f, 1.0f);
        private readonly Vector4 _colorCenter = new Vector4(0.8f, 0.8f, 0.8f, 0.8f);

        /// <summary>
        /// Gets or sets the current gizmo mode (translate, rotate, scale).
        /// Changing the mode regenerates the gizmo geometry.
        /// </summary>
        internal GizmoType CurrentMode
        {
            get => _currentMode;
            set
            {
                _currentMode = value;
                UpdateGizmoGeometry();
            }
        }

        /// <inheritdoc />
        public override async ValueTask OnStart(WorldRenderer renderer)
        {
            await InitializeGizmo(renderer).ConfigureAwait(false);
            Entity.Layer = IoC.Container.GetInstance<RenderLayerSystem>().Debug;
        }

        /// <summary>
        /// Creates the material and mesh renderer for the gizmo.
        /// </summary>
        private async ValueTask InitializeGizmo(WorldRenderer renderer)
        {
            _gizmoMaterial = await CreateGizmoMaterial(renderer).ConfigureAwait(false);
            _meshRenderer = Entity.AddComponent<MeshRenderer>();
            UpdateGizmoGeometry();
        }

        /// <inheritdoc />
        public override ValueTask Update(WorldRenderer renderer)
        {
            var pushConstants = new GizmoPushConstants
            {
                GizmoColor = GetCurrentGizmoColor(),
                GizmoType = (uint)_currentMode,
            };

            _gizmoMaterial.SetPushConstant("push", pushConstants);

            var fragConstants = new GizmoPushFragConstants
            {
                GizmoType = (uint)_currentMode,
                AxisMask = (uint)_selectedAxis
            };

            _gizmoMaterial.SetPushConstant("push_frag", fragConstants);

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Determines the color used to render the gizmo based on selection/hover state.
        /// </summary>
        private Vector4 GetCurrentGizmoColor()
        {
            if (_selectedAxis != GizmoAxis.None)
            {
                return _selectedAxis switch
                {
                    GizmoAxis.X => _colorX,
                    GizmoAxis.Y => _colorY,
                    GizmoAxis.Z => _colorZ,
                    GizmoAxis.Uniform => _colorUniform,
                    GizmoAxis.View => _colorCenter,
                    _ => new Vector4(0.8f, 0.8f, 0.8f, 1.0f)
                };
            }
            if (_hoveredAxis != GizmoAxis.None)
            {
                return _hoveredAxis switch
                {
                    GizmoAxis.X => _colorX,
                    GizmoAxis.Y => _colorY,
                    GizmoAxis.Z => _colorZ,
                    GizmoAxis.Uniform => _colorUniform,
                    GizmoAxis.View => _colorCenter,
                    _ => new Vector4(0.8f, 0.8f, 0.8f, 1.0f)
                };
            }

            return new Vector4(1, 1, 1, 1);
        }

        /// <summary>
        /// Rebuilds the gizmo mesh based on the current mode.
        /// </summary>
        private void UpdateGizmoGeometry()
        {
            var meshData = GenerateGizmoGeometry(_currentMode);
            var meshProvider = new MeshProvider<GizmoVertex>(meshData);
            _meshRenderer.SetProviders(meshProvider, new MaterialProvider(_gizmoMaterial));
        }

        /// <summary>
        /// Dispatches geometry generation to the appropriate method for the current mode.
        /// </summary>
        private MeshData<GizmoVertex> GenerateGizmoGeometry(GizmoType mode)
        {
            return mode switch
            {
                GizmoType.Translate => GenerateTranslateGizmo(),
                GizmoType.Rotate => GenerateRotateGizmo(),
                GizmoType.Scale => GenerateScaleGizmo(),
                _ => GenerateTranslateGizmo()
            };
        }

        /// <summary>
        /// Generates a translation gizmo: three coloured arrows and a centre cube.
        /// </summary>
        private MeshData<GizmoVertex> GenerateTranslateGizmo()
        {
            var vertices = new List<GizmoVertex>();
            var indices = new List<uint>();
            uint currentIndex = 0;

            float axisLength = 1.0f;
            float arrowHeadSize = 0.15f;
            float shaftRadius = 0.02f;
            float centerSize = 0.08f;

            GenerateArrow(vertices, indices, Vector3.UnitX, _colorX, axisLength, arrowHeadSize, shaftRadius, ref currentIndex, GizmoAxis.X);
            GenerateArrow(vertices, indices, Vector3.UnitY, _colorY, axisLength, arrowHeadSize, shaftRadius, ref currentIndex, GizmoAxis.Y);
            GenerateArrow(vertices, indices, Vector3.UnitZ, _colorZ, axisLength, arrowHeadSize, shaftRadius, ref currentIndex, GizmoAxis.Z);
            GenerateCube(vertices, indices, Vector3.Zero, _colorCenter, centerSize, ref currentIndex, GizmoAxis.Uniform);

            return new MeshData<GizmoVertex>(vertices.ToArray(), indices.ToArray());
        }

        /// <summary>
        /// Generates a rotation gizmo: three coloured rings and a centre sphere.
        /// </summary>
        private MeshData<GizmoVertex> GenerateRotateGizmo()
        {
            var vertices = new List<GizmoVertex>();
            var indices = new List<uint>();
            uint currentIndex = 0;

            float radius = 1.0f;
            float thickness = 0.08f;
            int segments = 48;

            GenerateRing(vertices, indices, Vector3.UnitX, _colorX, radius, thickness, segments, ref currentIndex, GizmoAxis.X);
            GenerateRing(vertices, indices, Vector3.UnitY, _colorY, radius, thickness, segments, ref currentIndex, GizmoAxis.Y);
            GenerateRing(vertices, indices, Vector3.UnitZ, _colorZ, radius, thickness, segments, ref currentIndex, GizmoAxis.Z);
            GenerateSphere(vertices, indices, Vector3.Zero, _colorCenter, 0.1f, 3, ref currentIndex, GizmoAxis.Uniform);

            return new MeshData<GizmoVertex>(vertices.ToArray(), indices.ToArray());
        }

        /// <summary>
        /// Generates a scale gizmo: three coloured lines with end cubes and a centre cube.
        /// </summary>
        private MeshData<GizmoVertex> GenerateScaleGizmo()
        {
            var vertices = new List<GizmoVertex>();
            var indices = new List<uint>();
            uint currentIndex = 0;

            float axisLength = 1.0f;
            float cubeSize = 0.1f;
            float shaftRadius = 0.015f;
            float centerSize = 0.08f;

            GenerateScaleHandle(vertices, indices, Vector3.UnitX, _colorX, axisLength, cubeSize, shaftRadius, ref currentIndex, GizmoAxis.X);
            GenerateScaleHandle(vertices, indices, Vector3.UnitY, _colorY, axisLength, cubeSize, shaftRadius, ref currentIndex, GizmoAxis.Y);
            GenerateScaleHandle(vertices, indices, Vector3.UnitZ, _colorZ, axisLength, cubeSize, shaftRadius, ref currentIndex, GizmoAxis.Z);
            GenerateCube(vertices, indices, Vector3.Zero, _colorUniform, centerSize, ref currentIndex, GizmoAxis.Uniform);
            MeshData<GizmoVertex> mesh = new MeshData<GizmoVertex>(vertices.ToArray(), indices.ToArray());
            return mesh;
        }

        // --- Geometry helpers (unchanged) ---
        private void GenerateArrow(List<GizmoVertex> vertices, List<uint> indices, Vector3 direction, Vector4 color, float length, float headSize, float shaftRadius, ref uint currentIndex, GizmoAxis axis)
        {
            if (axis == GizmoAxis.None || axis == GizmoAxis.Uniform)
            {
                return;
            }

            Vector3 start = Vector3.Zero;
            Vector3 shaftEnd = direction * (length - headSize);
            Vector3 headBase = shaftEnd;
            Vector3 headTip = direction * length;

            GenerateCylinder(vertices, indices, start, shaftEnd, shaftRadius, 8, color, ref currentIndex, axis);
            GenerateCone(vertices, indices, headBase, headTip, headSize * 0.6f, 8, color, ref currentIndex, axis);
        }

        private void GenerateScaleHandle(List<GizmoVertex> vertices, List<uint> indices, Vector3 direction, Vector4 color, float length, float cubeSize, float shaftRadius, ref uint currentIndex, GizmoAxis axis)
        {
            Vector3 start = Vector3.Zero;
            Vector3 shaftEnd = direction * (length - cubeSize * 0.5f);
            Vector3 cubePos = direction * length;

            GenerateCylinder(vertices, indices, start, shaftEnd, shaftRadius, 6, color, ref currentIndex, axis);
            GenerateCube(vertices, indices, cubePos, color, cubeSize, ref currentIndex, axis);
        }

        private static void GenerateRing(List<GizmoVertex> vertices, List<uint> indices, Vector3 normal, Vector4 color, float radius, float thickness, int segments, ref uint currentIndex, GizmoAxis axis)
        {
            Vector3 right, up;
            if (normal == Vector3.UnitX)
            {
                right = Vector3.UnitY;
                up = Vector3.UnitZ;
            }
            else if (normal == Vector3.UnitY)
            {
                right = Vector3.UnitX;
                up = Vector3.UnitZ;
            }
            else
            {
                right = Vector3.UnitX;
                up = Vector3.UnitY;
            }

            uint baseIndex = currentIndex;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * MathF.PI * 2;
                Vector3 outerPoint = right * MathF.Cos(angle) * (radius + thickness * 0.5f) +
                                     up * MathF.Sin(angle) * (radius + thickness * 0.5f);
                Vector3 innerPoint = right * MathF.Cos(angle) * (radius - thickness * 0.5f) +
                                     up * MathF.Sin(angle) * (radius - thickness * 0.5f);

                vertices.Add(new GizmoVertex(outerPoint, color, normal, axis));
                vertices.Add(new GizmoVertex(innerPoint, color, normal, axis));
                currentIndex += 2;
            }

            for (int i = 0; i < segments; i++)
            {
                uint currentOuter = baseIndex + (uint)(i * 2);
                uint currentInner = currentOuter + 1;
                uint nextOuter = baseIndex + (uint)((i + 1) * 2);
                uint nextInner = nextOuter + 1;

                indices.Add(currentOuter);
                indices.Add(nextOuter);
                indices.Add(currentInner);

                indices.Add(currentInner);
                indices.Add(nextOuter);
                indices.Add(nextInner);
            }
        }

        private static void GenerateCylinder(List<GizmoVertex> vertices, List<uint> indices, Vector3 start, Vector3 end, float radius, int sides, Vector4 color, ref uint currentIndex, GizmoAxis axis)
        {
            Vector3 direction = Vector3.Normalize(end - start);
            float length = Vector3.Distance(start, end);

            Vector3 perp1, perp2;
            if (MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.9f)
            {
                perp1 = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitX));
            }
            else
            {
                perp1 = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
            }

            perp2 = Vector3.Normalize(Vector3.Cross(direction, perp1));

            uint baseIndex = currentIndex;
            for (int i = 0; i <= sides; i++)
            {
                float angle = (float)i / sides * MathF.PI * 2;
                Vector3 offset = perp1 * MathF.Cos(angle) * radius + perp2 * MathF.Sin(angle) * radius;
                Vector3 normal = Vector3.Normalize(offset);

                vertices.Add(new GizmoVertex(start + offset, color, normal, axis));
                vertices.Add(new GizmoVertex(end + offset, color, normal, axis));
                currentIndex += 2;
            }

            for (int i = 0; i < sides; i++)
            {
                uint currentBottom = baseIndex + (uint)(i * 2);
                uint currentTop = currentBottom + 1;
                uint nextBottom = baseIndex + (uint)((i + 1) * 2);
                uint nextTop = nextBottom + 1;

                indices.Add(currentBottom);
                indices.Add(nextBottom);
                indices.Add(currentTop);

                indices.Add(currentTop);
                indices.Add(nextBottom);
                indices.Add(nextTop);
            }
        }

        private static void GenerateCone(List<GizmoVertex> vertices, List<uint> indices, Vector3 baseCenter, Vector3 tip, float baseRadius, int sides, Vector4 color, ref uint currentIndex, GizmoAxis axis)
        {
            Vector3 direction = Vector3.Normalize(tip - baseCenter);

            Vector3 perp1, perp2;
            if (MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.9f)
            {
                perp1 = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitX));
            }
            else
            {
                perp1 = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
            }

            perp2 = Vector3.Normalize(Vector3.Cross(direction, perp1));

            uint baseIndex = currentIndex;
            for (int i = 0; i <= sides; i++)
            {
                float angle = (float)i / sides * MathF.PI * 2;
                Vector3 point = perp1 * MathF.Cos(angle) * baseRadius + perp2 * MathF.Sin(angle) * baseRadius;
                Vector3 normal = Vector3.Normalize(point - direction * baseRadius);

                vertices.Add(new GizmoVertex(baseCenter + point, color, normal, axis));
                currentIndex++;
            }

            vertices.Add(new GizmoVertex(tip, color, direction, axis));
            uint tipIndex = currentIndex;
            currentIndex++;

            for (int i = 0; i < sides; i++)
            {
                uint currentBase = baseIndex + (uint)i;
                uint nextBase = baseIndex + (uint)((i + 1) % sides);

                indices.Add(currentBase);
                indices.Add(nextBase);
                indices.Add(tipIndex);
            }
        }

        private static void GenerateCube(List<GizmoVertex> vertices, List<uint> indices, Vector3 center, Vector4 color, float size, ref uint currentIndex, GizmoAxis axis)
        {
            float halfSize = size * 0.5f;
            Vector3[] corners = new Vector3[]
            {
                new Vector3(-halfSize, -halfSize, -halfSize), new Vector3( halfSize, -halfSize, -halfSize),
                new Vector3( halfSize,  halfSize, -halfSize), new Vector3(-halfSize,  halfSize, -halfSize),
                new Vector3(-halfSize, -halfSize,  halfSize), new Vector3( halfSize, -halfSize,  halfSize),
                new Vector3( halfSize,  halfSize,  halfSize), new Vector3(-halfSize,  halfSize,  halfSize)
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ,
                -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX,
                Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX,
                Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY,
                -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY
            };

            uint baseIndex = currentIndex;
            uint[][] faceIndices =
            [
                [0, 1, 2, 2, 3, 0], // front
                [5, 4, 7, 7, 6, 5], // back
                [4, 0, 3, 3, 7, 4], // left
                [1, 5, 6, 6, 2, 1], // right
                [3, 2, 6, 6, 7, 3], // top
                [4, 5, 1, 1, 0, 4]  // bottom
            ];

            for (int face = 0; face < 6; face++)
            {
                foreach (var cornerIndex in faceIndices[face])
                {
                    vertices.Add(new GizmoVertex(center + corners[cornerIndex], color, normals[face * 4], axis));
                }
            }

            for (uint i = 0; i < 36; i++)
            {
                indices.Add(baseIndex + i);
            }

            currentIndex += 36;
        }

        private static void GenerateSphere(List<GizmoVertex> vertices, List<uint> indices, Vector3 center, Vector4 color, float radius, int subdivisions, ref uint currentIndex, GizmoAxis axis)
        {
            float t = (1.0f + MathF.Sqrt(5.0f)) / 2.0f;

            Vector3[] baseVertices = new Vector3[]
            {
                new Vector3(-1,  t,  0), new Vector3(1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
                new Vector3( 0, -1,  t), new Vector3(0,  1,  t), new Vector3(0, -1, -t), new Vector3( 0,  1, -t),
                new Vector3( t,  0, -1), new Vector3(t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1)
            };

            uint[] baseIndices = new uint[]
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };

            uint baseIndex = currentIndex;
            foreach (var vertex in baseVertices)
            {
                Vector3 normalized = Vector3.Normalize(vertex);
                vertices.Add(new GizmoVertex(center + normalized * radius, color, normalized, axis));
                currentIndex++;
            }

            foreach (var index in baseIndices)
            {
                indices.Add(baseIndex + index);
            }
        }

        private async Task<Material> CreateGizmoMaterial(WorldRenderer renderer)
        {
            var shaderManager = IoC.Container.GetInstance<ShaderManager>();
            var material = new Material("Gizmo");

            using var vertShader = new Shader(renderer.Context, shaderManager.GetShader("Gizmo.vert"));
            using var fragShader = new Shader(renderer.Context, shaderManager.GetShader("Gizmo.frag"));

            var pipeline = CreateGizmoPipeline<PostLightPass>(renderer, renderer.RenderPass, vertShader, fragShader, "Gizmo");
            material.AddPass(PostLightPass.Name, new MaterialPass(pipeline));

            var vertPickingShader = new Shader(renderer.Context, shaderManager.GetShader("Gizmo.vert"));
            var fragPickingShader = new Shader(renderer.Context, shaderManager.GetShader("GizmoPicking.frag"));

            var pickingRenderPass = IoC.Container.GetInstance<PickingPassStrategy>().RenderPass;
            if (pickingRenderPass is not null)
            {
                var pickingPipeline = CreateGizmoPipeline<PickingSubPass>(renderer, pickingRenderPass, vertPickingShader, fragPickingShader, "GizmoPicking");
                material.AddPass(PickingSubPass.Name, new MaterialPass(pickingPipeline));
            }

            return material;
        }

        private RckPipeline CreateGizmoPipeline<T>(WorldRenderer renderer, RckRenderPass renderPass,
            Shader vertShader, Shader fragShader, string name) where T : IRenderSubPass
        {
            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault(
                VulkanContext.GetCurrent(),
                name,
                renderPass,
                [vertShader, fragShader]);

            pipelineBuilder
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(GizmoVertex.GetBindingDescription(), GizmoVertex.GetAttributeDescriptions()))
                .WithSubpass<T>()
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.Always,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false,
                })
                .WithRasterizer(new VulkanRasterizerBuilder()
                    .PolygonMode(PolygonMode.Fill)
                    .CullFace(CullModeFlags.None)
                    .FrontFace(FrontFace.Clockwise)
                    .DepthBiasEnabe(true)
                    .DepthBiasConstantFactor(-1.0f)
                    .DepthBiasClamp(0.0f)
                    .DepthBiasSlopeFactor(-1.0f))
                .WithInputAssembly(new VulkanInputAssemblyBuilder()
                    .Configure(topology: PrimitiveTopology.TriangleList))
                .WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(new PipelineColorBlendAttachmentState
                    {
                        BlendEnable = true,
                        SrcColorBlendFactor = BlendFactor.SrcAlpha,
                        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        ColorBlendOp = BlendOp.Add,
                        SrcAlphaBlendFactor = BlendFactor.One,
                        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        AlphaBlendOp = BlendOp.Add,
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                         ColorComponentFlags.BBit | ColorComponentFlags.ABit
                    }));

            return renderer.PipelineManager.Create(pipelineBuilder);
        }

        /// <summary>
        /// Begins a drag operation. Sets up the drag plane based on the selected axis and camera view.
        /// </summary>
        /// <param name="mousePosition">Current mouse position in screen coordinates.</param>
        /// <param name="camera">Active camera.</param>
        /// <param name="imageMin">Top‑left corner of the viewport image.</param>
        /// <param name="imageMax">Bottom‑right corner of the viewport image.</param>
        /// <param name="viewportSize">Total size of the viewport.</param>
        public void StartDrag(Vector2 mousePosition, Camera camera, Vector2 imageMin, Vector2 imageMax, Vector2 viewportSize)
        {
            if (_selectedAxis == GizmoAxis.None)
            {
                return;
            }

            _isDragging = true;
            _dragStartPosition = mousePosition;
            _currentImageMin = imageMin;
            _currentImageMax = imageMax;
            _viewportSize = viewportSize;

            var gizmoPos = Entity.Transform.Position;
            SetupDragPlane(camera, gizmoPos);
        }

        /// <summary>
        /// Constructs the plane used for dragging based on the selected axis.
        /// For single axes, the plane contains the axis and faces the camera.
        /// For uniform/centre handles, the plane is perpendicular to the camera forward direction.
        /// </summary>
        private void SetupDragPlane(Camera camera, Vector3 planePoint)
        {
            _dragPlanePoint = planePoint;
            var gizmoTransform = Entity.Transform;

            // Determine axis direction for the selected handle
            _dragAxisDirection = _selectedAxis switch
            {
                GizmoAxis.X => gizmoTransform.Right,
                GizmoAxis.Y => gizmoTransform.Up,
                GizmoAxis.Z => gizmoTransform.Forward,
                _ => Vector3.Zero
            };

            Vector3 cameraForward = camera.Entity.Transform.Forward;

            if (_selectedAxis == GizmoAxis.Uniform || _selectedAxis == GizmoAxis.None)
            {
                // Uniform translation: use the view plane (perpendicular to camera forward)
                _dragPlaneNormal = cameraForward;
            }
            else
            {
                // For a single axis, create a plane that contains the axis and faces the camera.
                // The plane normal is the cross product of the axis direction and camera forward.
                // If the axis is nearly parallel to the view direction, use an alternative up vector.
                Vector3 normal = Vector3.Cross(_dragAxisDirection, cameraForward);
                if (normal.LengthSquared() < 1e-6f)
                {
                    // Axis is parallel to view direction; fallback to a plane using world up.
                    normal = Vector3.Cross(_dragAxisDirection, Vector3.UnitY);
                    if (normal.LengthSquared() < 1e-6f)
                    {
                        normal = Vector3.Cross(_dragAxisDirection, Vector3.UnitX);
                    }
                }
                _dragPlaneNormal = Vector3.Normalize(normal);
            }
        }

        /// <summary>
        /// Updates the selected entity's transform during a drag operation.
        /// </summary>
        /// <param name="currentMousePos">Current mouse position.</param>
        /// <param name="camera">Active camera.</param>
        /// <param name="selectedEntity">The entity being manipulated.</param>
        /// <param name="imageMin">Viewport image top‑left.</param>
        /// <param name="imageMax">Viewport image bottom‑right.</param>
        /// <param name="viewportSize">Total viewport size.</param>
        public void UpdateDrag(Vector2 currentMousePos, Camera camera, Entity selectedEntity,
                               Vector2 imageMin, Vector2 imageMax, Vector2 viewportSize)
        {
            if (!_isDragging || selectedEntity == null)
            {
                return;
            }

            // Capture start values on first drag update
            if (_dragStartWorldPos == default)
            {
                _dragStartWorldPos = selectedEntity.Transform.Position;
                _dragStartRotation = selectedEntity.Transform.Rotation;
                _dragStartScale = selectedEntity.Transform.Scale;
            }

            Ray startRay = GetMouseRay(_dragStartPosition, camera, imageMin, imageMax, viewportSize);
            Ray currentRay = GetMouseRay(currentMousePos, camera, imageMin, imageMax, viewportSize);

            switch (_currentMode)
            {
                case GizmoType.Translate:
                    UpdateTranslation(startRay, currentRay, selectedEntity);
                    break;
                case GizmoType.Rotate:
                    UpdateRotation(startRay, currentRay, selectedEntity);
                    break;
                case GizmoType.Scale:
                    UpdateScale(startRay, currentRay, selectedEntity);
                    break;
            }
        }

        /// <summary>
        /// Ends the current drag operation and clears temporary state.
        /// </summary>
        public void EndDrag()
        {
            _isDragging = false;
            _dragStartWorldPos = default;
            _dragStartRotation = Quaternion.Identity;
            _dragStartScale = Vector3.Zero;
        }

        /// <summary>
        /// Converts a screen‑space mouse position into a world‑space ray.
        /// </summary>
        private Ray GetMouseRay(Vector2 mouseScreenPos, Camera camera,
                                Vector2 imageMin, Vector2 imageMax, Vector2 viewportSize)
        {
            Vector2 imagePos = (mouseScreenPos - imageMin) / (imageMax - imageMin);
            imagePos = Vector2.Clamp(imagePos, Vector2.Zero, Vector2.One);

            Vector2 ndc = new Vector2(
                imagePos.X * 2.0f - 1.0f,
                imagePos.Y * 2.0f - 1.0f
            );

            Matrix4x4.Invert(camera.ProjectionMatrix, out var invProj);
            Matrix4x4.Invert(camera.ViewMatrix, out var invView);

            Vector4 viewNear = Vector4.Transform(new Vector4(ndc, 0.0f, 1.0f), invProj);
            viewNear /= viewNear.W;
            Vector4 viewFar = Vector4.Transform(new Vector4(ndc, 1.0f, 1.0f), invProj);
            viewFar /= viewFar.W;

            Vector3 worldNear = Vector3.Transform(new Vector3(viewNear.X, viewNear.Y, viewNear.Z), invView);
            Vector3 worldFar = Vector3.Transform(new Vector3(viewFar.X, viewFar.Y, viewFar.Z), invView);

            return new Ray(worldNear, Vector3.Normalize(worldFar - worldNear));
        }

        /// <summary>
        /// Updates the entity's position based on the selected translation axis.
        /// Uses a plane containing the axis for single axes, or the view plane for uniform dragging.
        /// </summary>
        private void UpdateTranslation(Ray startRay, Ray currentRay, Entity selectedEntity)
        {
            var transform = selectedEntity.Transform;

            if (!RayPlaneIntersection(startRay, _dragPlanePoint, _dragPlaneNormal, out float tStart) ||
                !RayPlaneIntersection(currentRay, _dragPlanePoint, _dragPlaneNormal, out float tCurrent))
            {
                return;
            }

            Vector3 worldStart = startRay.GetPoint(tStart);
            Vector3 worldCurrent = currentRay.GetPoint(tCurrent);

            if (_selectedAxis == GizmoAxis.Uniform)
            {
                // Uniform translation: move freely on the plane
                Vector3 delta = worldCurrent - worldStart;
                transform.Position = _dragStartWorldPos + delta;
            }
            else
            {
                // Single‑axis translation: project the intersection points onto the axis line
                float distStart = Vector3.Dot(worldStart - _dragPlanePoint, _dragAxisDirection);
                float distCurrent = Vector3.Dot(worldCurrent - _dragPlanePoint, _dragAxisDirection);
                float delta = distCurrent - distStart;

                transform.Position = _dragStartWorldPos + _dragAxisDirection * delta;
            }
        }

        /// <summary>
        /// Updates the entity's rotation around the selected axis.
        /// The angle is computed by projecting mouse rays onto the plane perpendicular to the axis.
        /// </summary>
        private void UpdateRotation(Ray startRay, Ray currentRay, Entity selectedEntity)
        {
            var transform = selectedEntity.Transform;
            var gizmoTransform = Entity.Transform;

            Vector3 axis = _selectedAxis switch
            {
                GizmoAxis.X => gizmoTransform.Right,
                GizmoAxis.Y => gizmoTransform.Up,
                GizmoAxis.Z => gizmoTransform.Forward,
                _ => Vector3.Zero
            };

            Vector3 pivot = _dragStartWorldPos;

            if (!ProjectRayToPlane(startRay, pivot, axis, out Vector3 projStart) ||
                !ProjectRayToPlane(currentRay, pivot, axis, out Vector3 projCurrent))
            {
                return;
            }

            Vector3 vStart = projStart - pivot;
            Vector3 vCurrent = projCurrent - pivot;

            if (vStart.LengthSquared() < 0.0001f || vCurrent.LengthSquared() < 0.0001f)
            {
                return;
            }

            vStart = Vector3.Normalize(vStart);
            vCurrent = Vector3.Normalize(vCurrent);

            float dot = Vector3.Dot(vStart, vCurrent);
            Vector3 cross = Vector3.Cross(vStart, vCurrent);
            float angle = MathF.Atan2(Vector3.Dot(cross, axis), dot);

            Quaternion deltaRot = Quaternion.CreateFromAxisAngle(axis, angle);
            transform.Rotation = Quaternion.Normalize(deltaRot * _dragStartRotation);
        }

        /// <summary>
        /// Updates the entity's scale. For single axes, the scaling factor is derived from the
        /// projected distance along the axis on the drag plane. Uniform scaling uses the view plane.
        /// </summary>
        private void UpdateScale(Ray startRay, Ray currentRay, Entity selectedEntity)
        {
            var transform = selectedEntity.Transform;

            if (!RayPlaneIntersection(startRay, _dragPlanePoint, _dragPlaneNormal, out float tStart) ||
                !RayPlaneIntersection(currentRay, _dragPlanePoint, _dragPlaneNormal, out float tCurrent))
            {
                return;
            }

            Vector3 worldStart = startRay.GetPoint(tStart);
            Vector3 worldCurrent = currentRay.GetPoint(tCurrent);

            if (_selectedAxis == GizmoAxis.Uniform)
            {
                float distStart = Vector3.Distance(worldStart, _dragPlanePoint);
                float distCurrent = Vector3.Distance(worldCurrent, _dragPlanePoint);
                float factor = distCurrent / MathF.Max(distStart, 0.001f);
                transform.Scale = _dragStartScale * factor;
            }
            else
            {
                float distStart = Vector3.Dot(worldStart - _dragPlanePoint, _dragAxisDirection);
                float distCurrent = Vector3.Dot(worldCurrent - _dragPlanePoint, _dragAxisDirection);

                // Compute scale factor relative to start distance
                float startLen = MathF.Abs(distStart);
                if (startLen < 0.001f)
                {
                    startLen = 0.001f;
                }

                float factor = distCurrent / startLen;

                Vector3 newScale = _dragStartScale;
                if (_selectedAxis == GizmoAxis.X)
                {
                    newScale.X *= factor;
                }
                else if (_selectedAxis == GizmoAxis.Y)
                {
                    newScale.Y *= factor;
                }
                else if (_selectedAxis == GizmoAxis.Z)
                {
                    newScale.Z *= factor;
                }

                // Prevent zero or negative scale
                newScale = Vector3.Max(new Vector3(0.001f), newScale);
                transform.Scale = newScale;
            }
        }

        /// <summary>
        /// Helper: ray‑plane intersection.
        /// </summary>
        private static bool RayPlaneIntersection(Ray ray, Vector3 planePoint, Vector3 planeNormal, out float t)
        {
            float denom = Vector3.Dot(ray.Direction, planeNormal);
            if (MathF.Abs(denom) < 1e-6f)
            {
                t = 0;
                return false;
            }
            t = Vector3.Dot(planePoint - ray.Origin, planeNormal) / denom;
            return t >= 0;
        }

        /// <summary>
        /// Helper: projects a ray onto a plane defined by a point and normal.
        /// </summary>
        private static bool ProjectRayToPlane(Ray ray, Vector3 planePoint, Vector3 planeNormal, out Vector3 point)
        {
            point = Vector3.Zero;
            if (!RayPlaneIntersection(ray, planePoint, planeNormal, out float t))
            {
                return false;
            }

            point = ray.GetPoint(t);
            return true;
        }

        /// <summary>
        /// Sets the currently selected axis (used by picking).
        /// </summary>
        public void SetSelectedAxis(GizmoAxis axis) => _selectedAxis = axis;

        /// <summary>
        /// Sets the currently hovered axis (used for visual feedback).
        /// </summary>
        public void SetHoveredAxis(GizmoAxis axis) => _hoveredAxis = axis;

        /// <summary>
        /// Vertex structure for the gizmo mesh, containing position, normal, color, and axis mask.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        public struct GizmoVertex : IVertex
        {
            /// <summary>World position (w unused).</summary>
            public System.Numerics.Vector4 Position;
            /// <summary>Normal vector (w unused).</summary>
            public System.Numerics.Vector4 Normal;
            /// <summary>Vertex color.</summary>
            public Vector4 Color;
            /// <summary>Mask indicating which gizmo axis this vertex belongs to.</summary>
            public uint AxisMask;

            /// <summary>
            /// Constructs a new gizmo vertex.
            /// </summary>
            public GizmoVertex(Vector3 position, Vector4 color, Vector3 normal, GizmoAxis axis)
            {
                Position = new Vector4(position, 0);
                Color = color;
                Normal = new Vector4(normal, 0);
                AxisMask = (uint)axis;
            }

            /// <inheritdoc />
            public static VertexInputBindingDescription GetBindingDescription() => new()
            {
                Binding = 0,
                Stride = (uint)Marshal.SizeOf<GizmoVertex>(),
                InputRate = VertexInputRate.Vertex
            };

            /// <inheritdoc />
            public static VertexInputAttributeDescription[] GetAttributeDescriptions()
            {
                return new[]
                {
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 0,
                        Format = Format.R32G32B32Sfloat,
                        Offset = 0
                    },
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 1,
                        Format = Format.R32G32B32Sfloat,
                        Offset = (uint)Marshal.OffsetOf<GizmoVertex>(nameof(Normal))
                    },
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 2,
                        Format = Format.R32G32B32A32Sfloat,
                        Offset = (uint)Marshal.OffsetOf<GizmoVertex>(nameof(Color))
                    },
                    new VertexInputAttributeDescription
                    {
                        Binding = 0,
                        Location = 3,
                        Format = Format.R32Uint,
                        Offset = (uint)Marshal.OffsetOf<GizmoVertex>(nameof(AxisMask))
                    }
                };
            }
        }
    }
}
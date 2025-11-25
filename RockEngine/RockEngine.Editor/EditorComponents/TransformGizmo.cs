using RockEngine.Core;
using RockEngine.Core.Assets.AssetData;
using RockEngine.Core.Builders;
using RockEngine.Core.DI;
using RockEngine.Core.ECS;
using RockEngine.Core.ECS.Components;
using RockEngine.Core.Rendering;
using RockEngine.Core.Rendering.Materials;
using RockEngine.Core.Rendering.Objects;
using RockEngine.Core.Rendering.Passes.SubPasses;
using RockEngine.Core.ResourceProviders;
using RockEngine.Editor.Rendering.Passes;
using RockEngine.Editor.Rendering.Passes.SubPasses;
using RockEngine.Vulkan;

using Silk.NET.Vulkan;

using System.Numerics;
using System.Runtime.InteropServices;

namespace RockEngine.Editor.EditorComponents
{
    public enum GizmoType : uint
    {
        Translate = 0,
        Rotate = 1,
        Scale = 2
    }

    public enum GizmoAxis:uint
    {
        None = 0,
        X = 1,
        Y = 2,
        Z = 4,
        Uniform = 8,
        View = 16
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GizmoPushConstants
    {
        public Vector4 GizmoColor;
        public uint GizmoType;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct GizmoPushFragConstants
    {
        public uint GizmoType;
        public uint AxisMask;
    }

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
        private MeshRenderer _meshRenderer;

        // Colors for different axes
        private readonly Vector4 _colorX = new Vector4(0.9f, 0.2f, 0.2f, 1.0f);
        private readonly Vector4 _colorY = new Vector4(0.2f, 0.9f, 0.2f, 1.0f);
        private readonly Vector4 _colorZ = new Vector4(0.2f, 0.4f, 1.0f, 1.0f);
        private readonly Vector4 _colorUniform = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
        private readonly Vector4 _colorHover = new Vector4(1.0f, 0.9f, 0.2f, 1.0f);
        private readonly Vector4 _colorCenter = new Vector4(0.8f, 0.8f, 0.8f, 0.8f);


        internal GizmoType CurrentMode
        {
            get => _currentMode;
            set
            {
                _currentMode = value;
                UpdateGizmoGeometry();
            }
        }

        public override async ValueTask OnStart(WorldRenderer renderer)
        {
            await InitializeGizmo(renderer);
            Entity.Layer = IoC.Container.GetInstance<RenderLayerSystem>().Debug;
        }

        private async ValueTask InitializeGizmo(WorldRenderer renderer)
        {
            _gizmoMaterial = await CreateGizmoMaterial(renderer);
            _meshRenderer = Entity.AddComponent<MeshRenderer>();
            UpdateGizmoGeometry();
        }

        public override ValueTask Update(WorldRenderer renderer)
        {
            var pushConstants = new GizmoPushConstants
            {
                GizmoColor = GetCurrentGizmoColor(),
                GizmoType = (uint)_currentMode,
            };

            _gizmoMaterial.PushConstant("push", pushConstants);
            return ValueTask.CompletedTask;
        }

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

        private void UpdateGizmoGeometry()
        {
            var (vertices, indices) = GenerateGizmoGeometry(_currentMode);
            var meshProvider = new MeshProvider<GizmoVertex>(new MeshData<GizmoVertex>(vertices, indices));
            _meshRenderer.SetProviders(meshProvider, new MaterialProvider(_gizmoMaterial));
        }

        private (GizmoVertex[] vertices, uint[] indices) GenerateGizmoGeometry(GizmoType mode)
        {
            return mode switch
            {
                GizmoType.Translate => GenerateTranslateGizmo(),
                GizmoType.Rotate => GenerateRotateGizmo(),
                GizmoType.Scale => GenerateScaleGizmo(),
                _ => GenerateTranslateGizmo()
            };
        }

        private (GizmoVertex[] vertices, uint[] indices) GenerateTranslateGizmo()
        {
            var vertices = new List<GizmoVertex>();
            var indices = new List<uint>();
            uint currentIndex = 0;

            float axisLength = 1.0f;
            float arrowHeadSize = 0.15f;
            float shaftRadius = 0.02f;
            float centerSize = 0.08f;


            // X Axis (Red) - Use GizmoAxis.X for picking
            GenerateArrow(vertices, indices, Vector3.UnitX, _colorX, axisLength, arrowHeadSize, shaftRadius, ref currentIndex, GizmoAxis.X);

            // Y Axis (Green) - Use GizmoAxis.Y for picking  
            GenerateArrow(vertices, indices, Vector3.UnitY, _colorY, axisLength, arrowHeadSize, shaftRadius, ref currentIndex, GizmoAxis.Y);

            // Z Axis (Blue) - Use GizmoAxis.Z for picking
            GenerateArrow(vertices, indices, Vector3.UnitZ, _colorZ, axisLength, arrowHeadSize, shaftRadius, ref currentIndex, GizmoAxis.Z);

            // Center cube for view-plane movement - Use GizmoAxis.Uniform for picking
            GenerateCube(vertices, indices, Vector3.Zero, _colorCenter, centerSize, ref currentIndex, GizmoAxis.Uniform);
            return (vertices.ToArray(), indices.ToArray());
        }


        private (GizmoVertex[] vertices, uint[] indices) GenerateRotateGizmo()
        {
            var vertices = new List<GizmoVertex>();
            var indices = new List<uint>();
            uint currentIndex = 0;

            float radius = 1.0f;
            float thickness = 0.03f;
            int segments = 48;

            // X Rotation Ring (Red) - Use GizmoAxis.X
            GenerateRing(vertices, indices, Vector3.UnitX, _colorX, radius, thickness, segments, ref currentIndex, GizmoAxis.X);

            // Y Rotation Ring (Green) - Use GizmoAxis.Y
            GenerateRing(vertices, indices, Vector3.UnitY, _colorY, radius, thickness, segments, ref currentIndex, GizmoAxis.Y);

            // Z Rotation Ring (Blue) - Use GizmoAxis.Z
            GenerateRing(vertices, indices, Vector3.UnitZ, _colorZ, radius, thickness, segments, ref currentIndex, GizmoAxis.Z);

            // Center sphere - Use GizmoAxis.Uniform
            GenerateSphere(vertices, indices, Vector3.Zero, _colorCenter, 0.1f, 3, ref currentIndex, GizmoAxis.Uniform);

            return (vertices.ToArray(), indices.ToArray());
        }

        private (GizmoVertex[] vertices, uint[] indices) GenerateScaleGizmo()
        {
            var vertices = new List<GizmoVertex>();
            var indices = new List<uint>();
            uint currentIndex = 0;

            float axisLength = 1.0f;
            float cubeSize = 0.1f;
            float shaftRadius = 0.015f;
            float centerSize = 0.08f;

            // X Axis (Red) - Use GizmoAxis.X
            GenerateScaleHandle(vertices, indices, Vector3.UnitX, _colorX, axisLength, cubeSize, shaftRadius, ref currentIndex, GizmoAxis.X);

            // Y Axis (Green) - Use GizmoAxis.Y
            GenerateScaleHandle(vertices, indices, Vector3.UnitY, _colorY, axisLength, cubeSize, shaftRadius, ref currentIndex, GizmoAxis.Y);

            // Z Axis (Blue) - Use GizmoAxis.Z
            GenerateScaleHandle(vertices, indices, Vector3.UnitZ, _colorZ, axisLength, cubeSize, shaftRadius, ref currentIndex, GizmoAxis.Z);

            // Center cube for uniform scaling - Use GizmoAxis.Uniform
            GenerateCube(vertices, indices, Vector3.Zero, _colorUniform, centerSize, ref currentIndex, GizmoAxis.Uniform);

            return (vertices.ToArray(), indices.ToArray());
        }

        private void GenerateArrow(List<GizmoVertex> vertices, List<uint> indices, Vector3 direction, Vector4 color, float length, float headSize, float shaftRadius, ref uint currentIndex, GizmoAxis axis)
        {
            // Ensure we're using single axis flags
            if (axis == GizmoAxis.None || axis == GizmoAxis.Uniform)
            {
                return;
            }

            Vector3 start = Vector3.Zero;
            Vector3 shaftEnd = direction * (length - headSize);
            Vector3 headBase = shaftEnd;
            Vector3 headTip = direction * length;

            // Arrow shaft (cylinder)
            GenerateCylinder(vertices, indices, start, shaftEnd, shaftRadius, 8, color, ref currentIndex, axis);

            // Arrow head (cone)
            GenerateCone(vertices, indices, headBase, headTip, headSize * 0.6f, 8, color, ref currentIndex, axis);
        }


        private void GenerateScaleHandle(List<GizmoVertex> vertices, List<uint> indices, Vector3 direction, Vector4 color, float length, float cubeSize, float shaftRadius, ref uint currentIndex, GizmoAxis axis)
        {
            Vector3 start = Vector3.Zero;
            Vector3 shaftEnd = direction * (length - cubeSize * 0.5f);
            Vector3 cubePos = direction * length;

            // Shaft (cylinder)
            GenerateCylinder(vertices, indices, start, shaftEnd, shaftRadius, 6, color, ref currentIndex, axis);

            // Cube at end
            GenerateCube(vertices, indices, cubePos, color, cubeSize, ref currentIndex, axis);
        }

        private void GenerateRing(List<GizmoVertex> vertices, List<uint> indices, Vector3 normal, Vector4 color, float radius, float thickness, int segments, ref uint currentIndex, GizmoAxis axis)
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
            else // Z axis
            {
                right = Vector3.UnitX;
                up = Vector3.UnitY;
            }

            uint baseIndex = currentIndex;

            // Generate ring vertices (tube cross-section)
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

            // Generate ring indices (quads)
            for (int i = 0; i < segments; i++)
            {
                uint currentOuter = baseIndex + (uint)(i * 2);
                uint currentInner = currentOuter + 1;
                uint nextOuter = baseIndex + (uint)((i + 1) * 2);
                uint nextInner = nextOuter + 1;

                // First triangle
                indices.Add(currentOuter);
                indices.Add(nextOuter);
                indices.Add(currentInner);

                // Second triangle
                indices.Add(currentInner);
                indices.Add(nextOuter);
                indices.Add(nextInner);
            }
        }

        private void GenerateCylinder(List<GizmoVertex> vertices, List<uint> indices, Vector3 start, Vector3 end, float radius, int sides, Vector4 color, ref uint currentIndex, GizmoAxis axis)
        {
            Vector3 direction = Vector3.Normalize(end - start);
            float length = Vector3.Distance(start, end);

            // Find perpendicular vectors
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

            // Generate vertices for both ends
            for (int i = 0; i <= sides; i++)
            {
                float angle = (float)i / sides * MathF.PI * 2;
                Vector3 offset = perp1 * MathF.Cos(angle) * radius + perp2 * MathF.Sin(angle) * radius;
                Vector3 normal = Vector3.Normalize(offset);

                vertices.Add(new GizmoVertex(start + offset, color, normal, axis));
                vertices.Add(new GizmoVertex(end + offset, color, normal, axis));
                currentIndex += 2;
            }

            // Generate side triangles
            for (int i = 0; i < sides; i++)
            {
                uint currentBottom = baseIndex + (uint)(i * 2);
                uint currentTop = currentBottom + 1;
                uint nextBottom = baseIndex + (uint)((i + 1) * 2);
                uint nextTop = nextBottom + 1;

                // First triangle
                indices.Add(currentBottom);
                indices.Add(nextBottom);
                indices.Add(currentTop);

                // Second triangle
                indices.Add(currentTop);
                indices.Add(nextBottom);
                indices.Add(nextTop);
            }
        }

        private void GenerateCone(List<GizmoVertex> vertices, List<uint> indices, Vector3 baseCenter, Vector3 tip, float baseRadius, int sides, Vector4 color, ref uint currentIndex, GizmoAxis axis)
        {
            Vector3 direction = Vector3.Normalize(tip - baseCenter);

            // Find perpendicular vectors
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

            // Generate base vertices
            for (int i = 0; i <= sides; i++)
            {
                float angle = (float)i / sides * MathF.PI * 2;
                Vector3 point = perp1 * MathF.Cos(angle) * baseRadius + perp2 * MathF.Sin(angle) * baseRadius;
                Vector3 normal = Vector3.Normalize(point - direction * baseRadius);

                vertices.Add(new GizmoVertex(baseCenter + point, color, normal, axis));
                currentIndex++;
            }

            // Add tip vertex
            vertices.Add(new GizmoVertex(tip, color, direction, axis));
            uint tipIndex = currentIndex;
            currentIndex++;

            // Generate side triangles
            for (int i = 0; i < sides; i++)
            {
                uint currentBase = baseIndex + (uint)i;
                uint nextBase = baseIndex + (uint)((i + 1) % sides);

                indices.Add(currentBase);
                indices.Add(nextBase);
                indices.Add(tipIndex);
            }
        }

        private void GenerateCube(List<GizmoVertex> vertices, List<uint> indices, Vector3 center, Vector4 color, float size, ref uint currentIndex, GizmoAxis axis)
        {
            float halfSize = size * 0.5f;
            Vector3[] corners = new Vector3[]
            {
                new Vector3(-halfSize, -halfSize, -halfSize),
                new Vector3( halfSize, -halfSize, -halfSize),
                new Vector3( halfSize,  halfSize, -halfSize),
                new Vector3(-halfSize,  halfSize, -halfSize),
                new Vector3(-halfSize, -halfSize,  halfSize),
                new Vector3( halfSize, -halfSize,  halfSize),
                new Vector3( halfSize,  halfSize,  halfSize),
                new Vector3(-halfSize,  halfSize,  halfSize)
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, // front
                -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, // back
                -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitX, // left
                Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, // right
                Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, // top
                -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY  // bottom
            };

            uint[][] faceIndices = new uint[][]
            {
                [0, 1, 2, 2, 3, 0], // front
                [5, 4, 7, 7, 6, 5], // back
                [4, 0, 3, 3, 7, 4], // left
                [1, 5, 6, 6, 2, 1], // right
                [3, 2, 6, 6, 7, 3], // top
                [4, 5, 1, 1, 0, 4]  // bottom
            };

            uint baseIndex = currentIndex;

            // Add vertices for each face
            for (int face = 0; face < 6; face++)
            {
                foreach (var cornerIndex in faceIndices[face])
                {
                    vertices.Add(new GizmoVertex(center + corners[cornerIndex], color, normals[face * 4], axis));
                }
            }

            // Add indices
            for (uint i = 0; i < 36; i++)
            {
                indices.Add(baseIndex + i);
            }

            currentIndex += 36;
        }

        private void GenerateSphere(List<GizmoVertex> vertices, List<uint> indices, Vector3 center, Vector4 color, float radius, int subdivisions, ref uint currentIndex, GizmoAxis axis)
        {
            // Simple icosahedron-based sphere approximation
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

            // Add normalized vertices
            foreach (var vertex in baseVertices)
            {
                Vector3 normalized = Vector3.Normalize(vertex);
                vertices.Add(new GizmoVertex(center + normalized * radius, color, normalized, axis));
                currentIndex++;
            }

            // Add base indices
            foreach (var index in baseIndices)
            {
                indices.Add(baseIndex + index);
            }
        }

        private async Task<Material> CreateGizmoMaterial(WorldRenderer renderer)
        {
            var material = new Material("Gizmo");
            var vertShader = await VkShaderModule.CreateAsync(renderer.Context, "Shaders/Gizmo.vert.spv", ShaderStageFlags.VertexBit);
            var fragShader = await VkShaderModule.CreateAsync(renderer.Context, "Shaders/Gizmo.frag.spv", ShaderStageFlags.FragmentBit);

            var pipeline = CreateGizmoPipeline<PostLightPass>(renderer,renderer.RenderPass, vertShader, fragShader, "Gizmo");
            material.AddPass(PostLightPass.Name, new MaterialPass(pipeline));

            var vertPickingShader = await VkShaderModule.CreateAsync(renderer.Context, "Shaders/Gizmo.vert.spv", ShaderStageFlags.VertexBit);
            var fragPickingShader = await VkShaderModule.CreateAsync(renderer.Context, "Shaders/GizmoPicking.frag.spv", ShaderStageFlags.FragmentBit);
            
            var pickingRenderPass = IoC.Container.GetInstance<PickingPassStrategy>().RenderPass;
            if (pickingRenderPass is not null)
            {
                var pickingPipeline = CreateGizmoPipeline<PickingSubPass>(renderer, pickingRenderPass, vertPickingShader, fragPickingShader, "GizmoPicking");
                material.AddPass(PickingSubPass.Name, new MaterialPass(pickingPipeline));
            }
           

            return material;
        }

        private RckPipeline CreateGizmoPipeline<T>(WorldRenderer renderer, RckRenderPass renderPass, VkShaderModule vertShader, VkShaderModule fragShader, string name) where T : IRenderSubPass
        {
            using var pipelineBuilder = GraphicsPipelineBuilder.CreateDefault(
                VulkanContext.GetCurrent(),
                name,
                renderPass,
                [vertShader, fragShader]);

            // Make sure the vertex input state matches your GizmoVertex structure
            pipelineBuilder
                .WithVertexInputState(new VulkanPipelineVertexInputStateBuilder()
                    .Add(GizmoVertex.GetBindingDescription(), GizmoVertex.GetAttributeDescriptions()))
                .WithSubpass<T>()
                .AddDepthStencilState(new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = false,
                    DepthCompareOp = CompareOp.LessOrEqual,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false,
                })
                .WithRasterizer(new VulkanRasterizerBuilder()
                    .PolygonMode(PolygonMode.Fill)
                    .CullFace(CullModeFlags.None)
                    .FrontFace(FrontFace.Clockwise)
                    .DepthBiasEnabe(false))
                .WithInputAssembly(new VulkanInputAssemblyBuilder()
                    .Configure(topology: PrimitiveTopology.TriangleList))
                .WithColorBlendState(new VulkanColorBlendStateBuilder()
                    .AddAttachment(new PipelineColorBlendAttachmentState
                    {
                        BlendEnable = false,
                        SrcColorBlendFactor = BlendFactor.SrcAlpha,
                        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        ColorBlendOp = BlendOp.Add,
                        SrcAlphaBlendFactor = BlendFactor.One,
                        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                        AlphaBlendOp = BlendOp.Add,
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
                    }));

            return renderer.PipelineManager.Create(pipelineBuilder);
        }

        public void StartDrag(Vector2 mousePosition)
        {
            if (_selectedAxis == GizmoAxis.None)
            {
                return;
            }

            _isDragging = true;
            _dragStartPosition = mousePosition;
            _dragStartWorldPos = Entity.Transform.Position;
            _dragStartRotation = Entity.Transform.Rotation;
            _dragStartScale = Entity.Transform.Scale;
        }

        public void UpdateDrag(Vector2 mousePos, Camera camera, Entity selectedEntity)
        {
            if (!_isDragging || selectedEntity == null)
            {
                return;
            }

            var mouseDelta = _dragStartPosition - mousePos;

            switch (_currentMode)
            {
                case GizmoType.Translate:
                    UpdateTranslation(mouseDelta, camera, selectedEntity);
                    break;
                case GizmoType.Rotate:
                    UpdateRotation(mouseDelta, camera, selectedEntity);
                    break;
                case GizmoType.Scale:
                    UpdateScale(mouseDelta, camera, selectedEntity);
                    break;
            }
        }

        public void EndDrag()
        {
            _isDragging = false;
            _dragStartPosition = new Vector2(0);
        }

        private void UpdateTranslation(Vector2 mouseDelta, Camera camera, Entity selectedEntity)
        {
            var transform = selectedEntity.Transform;

            // Convert screen delta to world space movement
            float worldSensitivity = CalculateWorldSensitivity(camera, transform.Position);

            // Get the gizmo's transform to use its local axes
            var gizmoTransform = Entity.Transform;
            Vector3 movement = Vector3.Zero;

            if (_selectedAxis == GizmoAxis.X)
            {
                // Move along X axis in world space
                movement = gizmoTransform.Right * mouseDelta.X * worldSensitivity;
            }
            else if (_selectedAxis == GizmoAxis.Y)
            {
                // Move along Y axis in world space  
                movement = gizmoTransform.Up * mouseDelta.Y * worldSensitivity;
            }
            else if (_selectedAxis == GizmoAxis.Z)
            {
                // Move along Z axis in world space
                movement = gizmoTransform.Forward * mouseDelta.Y * worldSensitivity;
            }
            else if (_selectedAxis == GizmoAxis.Uniform)
            {
                // Move in view plane (screen space)
                var cameraTransform = camera.Entity.Transform;
                movement = (cameraTransform.Right * mouseDelta.X + cameraTransform.Up * -mouseDelta.Y) * worldSensitivity;
            }

            transform.Position = _dragStartWorldPos + movement;
        }

        private float CalculateWorldSensitivity(Camera camera, Vector3 worldPosition)
        {
            // Calculate sensitivity based on distance from camera
            float distance = Vector3.Distance(camera.Entity.Transform.Position, worldPosition);

            // Base sensitivity that works well at different distances
            float baseSensitivity = 0.01f;

            // Scale by distance but clamp to reasonable values
            float sensitivity = baseSensitivity * Math.Max(distance * 0.1f, 0.1f);

            return sensitivity;
        }


        private void UpdateRotation(Vector2 mouseDelta, Camera camera, Entity selectedEntity)
        {
            var transform = selectedEntity.Transform;

            // Rotation sensitivity (degrees per pixel)
            float sensitivity = 0.5f;

            Vector3 rotationDelta = Vector3.Zero;

            if (_selectedAxis == GizmoAxis.X)
            {
                // Rotate around X axis (pitch)
                rotationDelta.X = mouseDelta.Y * sensitivity;
            }
            else if (_selectedAxis == GizmoAxis.Y)
            {
                // Rotate around Y axis (yaw)
                rotationDelta.Y = mouseDelta.X * sensitivity;
            }
            else if (_selectedAxis == GizmoAxis.Z)
            {
                // Rotate around Z axis (roll)
                rotationDelta.Z = mouseDelta.X * sensitivity;
            }

            // Convert to radians
            Vector3 radians = rotationDelta * (MathF.PI / 180.0f);

            // Create rotation quaternions for each axis
            Quaternion deltaRotation = Quaternion.Identity;

            if (rotationDelta.X != 0)
            {
                deltaRotation *= Quaternion.CreateFromAxisAngle(Entity.Transform.Right, radians.X);
            }

            if (rotationDelta.Y != 0)
            {
                deltaRotation *= Quaternion.CreateFromAxisAngle(Entity.Transform.Up, radians.Y);
            }

            if (rotationDelta.Z != 0)
            {
                deltaRotation *= Quaternion.CreateFromAxisAngle(Entity.Transform.Forward, radians.Z);
            }

            // Apply the rotation
            transform.Rotation = Quaternion.Normalize(_dragStartRotation * deltaRotation);

        }

        private void UpdateScale(Vector2 mouseDelta, Camera camera, Entity selectedEntity)
        {
            var transform = selectedEntity.Transform;

            float sensitivity = 0.01f;
            Vector3 scaleDelta = Vector3.Zero;

            if (_selectedAxis == GizmoAxis.Uniform)
            {
                // Uniform scaling
                float uniformDelta = (mouseDelta.X + mouseDelta.Y) * sensitivity;
                scaleDelta = new Vector3(uniformDelta);
                transform.Scale = Vector3.Max(new Vector3(0.001f), _dragStartScale + scaleDelta);
            }
            else
            {
                // Non-uniform scaling based on selected axis
                if (_selectedAxis == GizmoAxis.X)
                {
                    scaleDelta.X = mouseDelta.X * sensitivity;
                }
                else if (_selectedAxis == GizmoAxis.Y)
                {
                    scaleDelta.Y = mouseDelta.Y * sensitivity;
                }
                else if (_selectedAxis == GizmoAxis.Z)
                {
                    scaleDelta.Z = mouseDelta.Y * sensitivity; // Use Y for consistency
                }

                // Apply scaling in local space
                transform.Scale = Vector3.Max(new Vector3(0.001f),
                    new Vector3(
                        _dragStartScale.X + scaleDelta.X,
                        _dragStartScale.Y + scaleDelta.Y,
                        _dragStartScale.Z + scaleDelta.Z
                    ));
            }
        }

        public void SetSelectedAxis(GizmoAxis axis)
        {
            _selectedAxis = axis;
        }

        public void SetHoveredAxis(GizmoAxis axis)
        {
            _hoveredAxis = axis;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GizmoVertex : IVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector4 Color;
            public uint AxisMask;

            public GizmoVertex(Vector3 position, Vector4 color, Vector3 normal, GizmoAxis axis)
            {
                Position = position;
                Color = color;
                Normal = normal;
                AxisMask = (uint)axis;
            }

            public static VertexInputBindingDescription GetBindingDescription() => new()
            {
                Binding = 0,
                Stride = (uint)Marshal.SizeOf<GizmoVertex>(),
                InputRate = VertexInputRate.Vertex
            };

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
        public static class MathHelper
        {
            public static float DegreesToRadians(float degrees)
            {
                return degrees * (MathF.PI / 180.0f);
            }
        }
    }
}
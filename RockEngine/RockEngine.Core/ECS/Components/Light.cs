using RockEngine.Core.Rendering;
using RockEngine.Vulkan;

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

    public class Light : Component
    {
        private static UniformBuffer? _buffer;
        public LightType Type = LightType.Point;
        public Vector3 Color = Vector3.One;
        public float Intensity = 1.0f;

        // Directional/Spot properties
        public Vector3 Direction = Vector3.UnitZ;

        // Point/Spot properties
        public float Radius = 10.0f;

        // Spot specific
        public float InnerCutoff = 0.9f;  // Cos(25 degrees)
        public float OuterCutoff = 0.7f;  // Cos(45 degrees)

        private static ulong _offsetCounter = 0; // Counter for offset allocation
        private LightData _lightData;
        private readonly ulong _offset; // Store the offset for this instance

        public ulong Offset => _offset;

        public Light()
        {
            _offset = _offsetCounter; // Assign the next available offset
            _offsetCounter += LightData.DataSize; // Increment for the next instance
        }

        public override ValueTask OnStart(Renderer renderer)
        {
            _buffer ??= new UniformBuffer(VulkanContext.GetCurrent(), "Lights", 0, Renderer.MAX_LIGHTS_SUPPORTED * LightData.DataSize, (int)LightData.DataSize, true);
            renderer.LightManager.RegisterLight(this);
            return base.OnStart(renderer);
        }

        public override ValueTask Update(Renderer renderer)
        {
            _lightData = new LightData
            {
                PositionAndType = new Vector4(Entity.Transform.WorldPosition, (float)Type),
                DirectionAndRadius = new Vector4(Direction, Radius),
                ColorAndIntensity = new Vector4(Color, Intensity),
                Cutoffs = new Vector2(InnerCutoff, OuterCutoff)
            };
            return default;
        }

        public ref LightData GetLightData()
        {
            return ref _lightData;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LightData
    {
        public static ulong DataSize { get; } = (ulong)Marshal.SizeOf<LightData>();
        // 16 bytes - Position (XYZ) + Type (packed in W as float)
        public Vector4 PositionAndType;

        // 16 bytes - Direction (XYZ) + Radius (W)
        public Vector4 DirectionAndRadius;

        // 16 bytes - Color (RGB) + Intensity (A)
        public Vector4 ColorAndIntensity;

        // 8 bytes - Cutoffs + Padding
        public Vector2 Cutoffs;
        private Vector2 _padding;
    }
}
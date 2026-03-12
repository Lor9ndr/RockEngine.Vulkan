using RockEngine.Core.Diagnostics;

namespace RockEngine.Core.Rendering.Passes
{
    public struct PipelineStatisticsData
    {
        public ulong InputAssemblyVertices;
        public ulong InputAssemblyPrimitives;
        public ulong VertexShaderInvocations;
        public ulong ClippingInvocations;
        public ulong ClippingPrimitives;
        public ulong FragmentShaderInvocations;
        public ulong ComputeShaderInvocations;
        public ulong GeometryShaderInvocations;
        public ulong GeometryShaderPrimitives;
        public ulong TessellationControlShaderPatches;
        public ulong TessellationEvaluationShaderInvocations;

        public DateTime CollectionTime;
        public uint FrameIndex;
        public string PassName;

        public readonly float VertexToFragmentRatio => FragmentShaderInvocations > 0 ?
            (float)VertexShaderInvocations / FragmentShaderInvocations : 0;
        public readonly float PrimitiveEfficiency => InputAssemblyPrimitives > 0 ?
            (float)ClippingPrimitives / InputAssemblyPrimitives : 0;
    }
}
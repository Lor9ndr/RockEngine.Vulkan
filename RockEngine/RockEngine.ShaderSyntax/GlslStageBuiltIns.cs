using System.Collections.Generic;

namespace RockEngine.ShaderSyntax
{
    internal static class GlslStageBuiltIns
    {
        // Variables common to all stages
        public static readonly HashSet<string> CommonVariables = new HashSet<string>
        {
            "gl_DepthRange",
            "gl_NumSamples"
        };

        public static readonly Dictionary<ShaderStage, HashSet<string>> StageVariables =
            new Dictionary<ShaderStage, HashSet<string>>
            {
                [ShaderStage.Vertex] = new HashSet<string>
                {
                    "gl_Position",
                    "gl_PointSize",
                    "gl_ClipDistance",
                    "gl_CullDistance",
                    "gl_VertexID",
                    "gl_InstanceID",
                    "gl_BaseVertex",
                    "gl_BaseInstance",
                    "gl_DrawID"
                },
                [ShaderStage.Fragment] = new HashSet<string>
                {
                    "gl_FragCoord",
                    "gl_FrontFacing",
                    "gl_PointCoord",
                    "gl_FragDepth",
                    "gl_SampleID",
                    "gl_SamplePosition",
                    "gl_SampleMask",
                    "gl_SampleMaskIn",
                    "gl_HelperInvocation",
                    "gl_Layer",
                    "gl_PrimitiveID",
                    "gl_ViewportIndex"
                },
                [ShaderStage.Compute] = new HashSet<string>
                {
                    "gl_NumWorkGroups",
                    "gl_WorkGroupID",
                    "gl_LocalInvocationID",
                    "gl_GlobalInvocationID",
                    "gl_LocalInvocationIndex",
                    "gl_WorkGroupSize"
                },
                // Add other stages as needed
            };
    }
}
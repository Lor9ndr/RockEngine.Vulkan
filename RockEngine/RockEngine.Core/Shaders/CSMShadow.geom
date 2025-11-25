#version 460
#extension GL_ARB_separate_shader_objects : enable

layout(triangles, invocations = 4) in; // 4 cascades
layout(triangle_strip, max_vertices = 3) out;

layout(std140, set = 0, binding = 0) uniform LightSpaceMatrices {
    mat4 lightSpaceMatrices[16];
};

void main() {          
    // Only process if this cascade is within our cascade count
    // We use 4 invocations but only process the actual cascades we have
    for (int i = 0; i < 3; ++i)
    {
        // Use gl_InvocationID to select the cascade matrix
        gl_Position = lightSpaceMatrices[gl_InvocationID] * gl_in[i].gl_Position;
        gl_Layer = gl_InvocationID; // Render to appropriate cascade layer
        EmitVertex();
    }
    EndPrimitive();
}
// Vertex shader unchanged

// Geometry shader
#version 460
#extension GL_ARB_separate_shader_objects : enable
#extension GL_EXT_scalar_block_layout : enable    // needed for flexible array

layout(triangles) in;
layout(triangle_strip, max_vertices = 18) out;

layout(push_constant) uniform ShadowPC {
    vec4 lightPos;
    float farPlane;
    uint shadowIndex;
} pc;

// Storage buffer with unsized array – accessed by base index + face
layout(set = 0, binding = 0, scalar) readonly buffer ShadowMatricesBlock {
    mat4 matrices[];
} shadowMatrices;

layout(location = 0) in vec3 fragPos[];
layout(location = 1) out vec4 fragPosLightSpace;
layout(location = 2) out vec3 worldPos;

void main() {
    uint baseIndex = pc.shadowIndex * 6;
    for (int face = 0; face < 6; ++face) {
        gl_Layer = face;
        mat4 shadowMat = shadowMatrices.matrices[baseIndex + face];
        for (int i = 0; i < 3; ++i) {
            worldPos = fragPos[i];
            fragPosLightSpace = shadowMat * vec4(fragPos[i], 1.0);
            gl_Position = fragPosLightSpace;
            EmitVertex();
        }
        EndPrimitive();
    }
}
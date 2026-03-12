#version 460
#extension GL_ARB_separate_shader_objects : enable

layout(triangles) in;
layout(triangle_strip, max_vertices = 18) out;

layout(push_constant) uniform ShadowPC {
    vec4 lightPos;
    float farPlane;
    uint shadowIndex;
} pc;

layout(set = 0, binding = 0) uniform ShadowMatrices {
    mat4 matrices[6];
} shadowMatrices;

layout(location = 0) in vec3 fragPos[];

layout(location = 1) out vec4 fragPosLightSpace;
layout(location = 2) out vec3 worldPos;

void main() {
    for(int face = 0; face < 6; ++face) {
        gl_Layer = face; // specifies which face we render to
        mat4 shadowMat = shadowMatrices.matrices[face];
        for(int i = 0; i < 3; ++i) { // for each triangle vertex
            worldPos = fragPos[i];
            fragPosLightSpace = shadowMat * vec4(fragPos[i], 1.0);
            gl_Position = fragPosLightSpace;
            EmitVertex();
        }
        EndPrimitive();
    }
}
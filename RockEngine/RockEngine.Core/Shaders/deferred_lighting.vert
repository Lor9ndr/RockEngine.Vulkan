#version 450 core
#extension GL_KHR_vulkan_glsl : enable


layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {
    mat4 viewProj;
    vec3 camPos;
} ubo;

layout(location = 0) out vec3 cameraPosition;

void main() {
    cameraPosition = ubo.camPos;
    vec2 positions[3] = vec2[](
        vec2(-1.0, -1.0),
        vec2(3.0, -1.0),
        vec2(-1.0, 3.0)
    );
    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
}
#version 460
#extension GL_KHR_vulkan_glsl : enable

layout(location = 0) in vec3 aPosition;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
    float gridScale;
    mat4 viewProj;
    mat4 model;
} push;

layout(location = 0) out vec3 vWorldPos;

void main() {
    // Transform vertex to world space
    vec4 worldPos = push.model * vec4(aPosition, 1.0);
    
    // Transform to clip space
    gl_Position = push.viewProj * worldPos;
    
    // Pass world position to fragment shader
    vWorldPos = worldPos.xyz;
}
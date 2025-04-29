#version 450
#extension GL_KHR_vulkan_glsl : enable

layout(set = 0, binding = 0) uniform GlobalUbo {
    mat4 view;
    mat4 proj;
    mat4 viewProj;
    vec3 camPos;
} ubo;

layout(location = 0) out vec3 fragTexCoord;

void main() {
    // Full-screen triangle positions
    vec2 positions[3] = vec2[](
        vec2(-1.0, -1.0),
        vec2(3.0, -1.0),
        vec2(-1.0, 3.0)
    );
    
    // Get position in NDC space
    vec4 pos = vec4(positions[gl_VertexIndex], 0.0, 1.0);
    
    // Remove translation from view matrix
    mat4 viewRotation = mat4(mat3(ubo.view));
    mat4 invViewProj = inverse(ubo.proj * viewRotation);
    
    // Calculate world position
    vec4 worldPos = invViewProj * pos;
    fragTexCoord = normalize(worldPos.xyz / worldPos.w);
    
    gl_Position = pos;
}
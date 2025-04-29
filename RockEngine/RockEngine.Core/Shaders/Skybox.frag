#version 450
#extension GL_KHR_vulkan_glsl : enable
#extension GL_ARB_separate_shader_objects : enable

layout(set = 1, binding = 0) uniform samplerCube uSkybox;

layout(location = 0) in vec3 fragTexCoord;
layout(location = 0) out vec4 outColor;

void main() {
    vec3 color = texture(uSkybox, fragTexCoord).rgb;
    
    // Optional: Apply gamma correction and tone mapping to match existing pipeline
    color = pow(color, vec3(2.2)); // Convert from sRGB to linear
    color = color / (color + vec3(1.0)); // Reinhard tone mapping
    color = pow(color, vec3(1.0/2.2)); // Convert back to sRGB
    
    outColor = vec4(color, 1.0);
}
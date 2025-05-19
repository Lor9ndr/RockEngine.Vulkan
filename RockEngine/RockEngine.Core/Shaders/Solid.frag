#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) out vec4 outColor;

// PBR material properties
layout(push_constant) uniform PushConstants {
    vec3 lightColor;
} color;


void main() 
{
    outColor = vec4(color.lightColor, 1.0);
}

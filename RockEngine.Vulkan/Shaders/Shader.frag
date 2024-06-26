#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec3 fragColor;
layout(location = 1) in vec2 texCoords;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D texSampler;

void main() {
    outColor = vec4(texture(texSampler, texCoords).rgb, 1.0);
}
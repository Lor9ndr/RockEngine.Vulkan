#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec2 texCoords;
layout(location = 1) in vec3 normal;

layout(location = 0) out vec4 outColor;

//layout(set = 2, binding = 1) uniform sampler2D normalSampler;
//layout(set = 2, binding = 2) uniform sampler2D specularSampler;

void main() {

    outColor = vec4(normal, 1.0);
}

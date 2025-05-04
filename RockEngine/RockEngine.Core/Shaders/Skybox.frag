#version 460

layout(location = 0) in vec3 fragPos;
layout(location = 0) out vec4 outColor; // Single output for Subpass 2

layout(set = 2, binding = 0) uniform samplerCube cubemapTex;

void main() {
    vec3 direction = normalize(fragPos);
    outColor = texture(cubemapTex, direction);
}
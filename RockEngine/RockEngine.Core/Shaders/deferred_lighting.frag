#version 450
#extension GL_KHR_vulkan_glsl : enable

layout(set = 0, binding = 0) uniform sampler2D gPosition;
layout(set = 0, binding = 1) uniform sampler2D gNormal;
layout(set = 0, binding = 2) uniform sampler2D gAlbedo;

layout(location = 0) out vec4 outColor;

void main() {
    ivec2 texCoord = ivec2(gl_FragCoord.xy);
    vec3 fragPos = texelFetch(gPosition, texCoord, 0).rgb;
    vec3 normal = normalize(texelFetch(gNormal, texCoord, 0).rgb);
    vec3 albedo = texelFetch(gAlbedo, texCoord, 0).rgb;

    // Simple directional light
    vec3 lightDir = normalize(vec3(1.0, 1.0, 1.0));
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 color = diff * albedo * vec3(1.0);

    outColor = vec4(color, 1.0);
}
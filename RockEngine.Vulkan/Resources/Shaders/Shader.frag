#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec2 texCoords;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D albedoSampler;
//layout(set = 2, binding = 2) uniform sampler2D specularSampler;

void main() {
    vec3 albedo = texture(albedoSampler, texCoords).rgb;
    //vec3 specular = texture(specularSampler, texCoords).rgb;

    //For demonstration, we will just mix these textures
    //vec3 finalColor = mix(albedo, normal, 0.5);

    outColor = vec4(albedo, 1.0);
}

#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec4 fragPos;
layout(location = 1) in vec2 texCoords;
layout(location = 2) in vec3 normals;

layout(location = 0) out vec4 outColor;

// PBR material properties
layout(set = 2, binding = 0) uniform sampler2D albedoMap;
layout(set = 2, binding = 1) uniform sampler2D metallicMap;
layout(set = 2, binding = 2) uniform sampler2D roughnessMap;

// Sunlight properties
const vec3 lightDirection = normalize(vec3(0.5, 0.5, 0.5)); // Direction of the sunlight
const vec3 lightColor = vec3(1.0, 0.9, 0.7); // Warm sunlight color
const float lightIntensity = 1.5; // Intensity of the sunlight

void main() {
    // Sample the albedo, metallic, and roughness textures
    vec4 albedoRgba = texture(albedoMap, texCoords);
    vec3 albedo = albedoRgba.rgb;
    float metallic = texture(metallicMap, texCoords).r;
    float roughness = texture(roughnessMap, texCoords).r;

    // Calculate the diffuse lighting
    vec3 normal = normalize(normals);
    float NdotL = max(dot(normal, lightDirection), 0.0);
    vec3 diffuse = lightColor * albedo * NdotL * lightIntensity;

    if(albedoRgba.a < 0.5)
    {
        discard;
    }
    // Combine the properties into a single color output
    outColor = vec4(diffuse, 1.0);
}

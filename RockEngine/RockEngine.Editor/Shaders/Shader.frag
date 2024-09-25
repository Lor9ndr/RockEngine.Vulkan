#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec4 fragPos;
layout(location = 1) in vec2 texCoords;
layout(location = 2) in vec3 normals;
layout(location = 3) in vec3 viewPos;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D albedoSampler;
layout(set = 2, binding = 1) uniform sampler2D normalSampler;

layout(set = 3, binding = 0) uniform LightData {
    vec3 position;
    vec3 color;
    float intensity;
    int type;
} light;

void main() {
    // Sample albedo and normal textures
    vec4 albedo = texture(albedoSampler, texCoords);
    vec3 normal = normalize(texture(normalSampler, texCoords).rgb * 2.0 - 1.0);

    // Ambient lighting
    vec3 ambient = light.color * light.intensity; //* material.ambient;

    // Diffuse lighting
    vec3 lightDir = normalize(light.position - fragPos.xyz);
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = light.color * light.intensity * diff ;//* material.diffuse);

    // Specular lighting
    vec3 viewDir = normalize(viewPos - fragPos.xyz); // Assuming camera is at (0,0,0) in view space
    vec3 reflectDir = reflect(-lightDir, normal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 1.2);//material.shininess);
    vec3 specular = light.color * light.intensity * spec ; //* material.specular);

    // Combine lighting components
    vec3 result = (ambient + diffuse + specular) * albedo.rgb;

    // Apply light attenuation based on distance (for point lights)
    if (light.type == 0) { // Assuming 0 is point light
        float distance = length(light.position - fragPos.xyz);
        float attenuation = 1.0 / (1.0 + 0.09 * distance + 0.032 * (distance * distance));
        result *= attenuation;
    }

    outColor = vec4(result, albedo.a);
}

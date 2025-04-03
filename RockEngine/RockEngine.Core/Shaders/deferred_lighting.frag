#version 450
#extension GL_KHR_vulkan_glsl : enable
#pragma shader_stage(fragment)
#extension GL_ARB_separate_shader_objects : enable

layout(input_attachment_index = 0, set = 2, binding = 0) uniform subpassInput gPosition;
layout(input_attachment_index = 1, set = 2, binding = 1) uniform subpassInput gNormal;
layout(input_attachment_index = 2, set = 2, binding = 2) uniform subpassInput gAlbedo;

layout(location = 0) in vec3 camPos;

struct LightData {
    vec4 positionAndType;      // xyz: position, w: type (as float)
    vec4 directionAndRadius;   // xyz: direction, w: radius
    vec4 colorAndIntensity;    // rgb: color, a: intensity
    vec2 cutoffs;              // x: inner cutoff, y: outer cutoff
};

layout(std430, set = 1, binding = 0) readonly buffer LightBuffer {
    LightData lights[];
};

layout(set = 1, binding = 1) uniform LightCount {
    int numLights;
};

layout(location = 0) out vec4 outColor;

const float specularStrength = 0.5;
const float specularPower = 128.0;
const float MIN_LIGHT_THRESHOLD = 0.001;

vec3 CalculateDirectionalLight(LightData light, vec3 normal, vec3 albedo, vec3 viewDir, float specIntensity) {
    vec3 lightDir = normalize(-light.directionAndRadius.xyz);
    
    // Diffuse calculation
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * albedo * light.colorAndIntensity.rgb * light.colorAndIntensity.a;
    
    // Specular calculation (Blinn-Phong)
    vec3 halfwayDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfwayDir), 0.0), specularPower);
    vec3 specular = specularStrength * spec * light.colorAndIntensity.rgb * specIntensity * light.colorAndIntensity.a;
    
    return diffuse + specular;
}

vec3 CalculatePointLight(LightData light, vec3 fragPos, vec3 normal, vec3 albedo, vec3 viewDir, float specIntensity) {
    vec3 lightVec = light.positionAndType.xyz - fragPos;
    float distance = length(lightVec);
    if(distance > light.directionAndRadius.w) return vec3(0.0);
    
    vec3 lightDir = normalize(lightVec);
    
    // Diffuse calculation
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * albedo * light.colorAndIntensity.rgb * light.colorAndIntensity.a;
    
    // Specular calculation
    vec3 reflectDir = reflect(-lightDir, normal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), specularPower);
    vec3 specular = specularStrength * spec * light.colorAndIntensity.rgb * specIntensity * light.colorAndIntensity.a;
    
    // Attenuation calculation
    float attenuation = 1.0 / (1.0 + 0.07 * distance + 0.017 * (distance * distance));
    attenuation *= smoothstep(light.directionAndRadius.w, light.directionAndRadius.w * 0.75, distance);
    
    return (diffuse + specular) * attenuation;
}
vec3 octDecode(vec2 encoded) {
    vec2 f = encoded * 2.0 - 1.0;
    vec3 n = vec3(f, 1.0 - abs(f.x) - abs(f.y));
    
    if (n.z < 0.0) {
        n.xy = (1.0 - abs(n.yx)) * sign(n.xy);
    }
    
    return normalize(n);
}


vec3 CalculateSpotLight(LightData light, vec3 fragPos, vec3 normal, vec3 albedo, vec3 viewDir, float specIntensity) {
    vec3 lightVec = light.positionAndType.xyz - fragPos;
    float distance = length(lightVec);
    if(distance > light.directionAndRadius.w) return vec3(0.0);
    
    vec3 lightDir = normalize(lightVec);
    float theta = dot(lightDir, normalize(-light.directionAndRadius.xyz));
    
    if(theta < light.cutoffs.y) return vec3(0.0);
    
    float epsilon = light.cutoffs.x - light.cutoffs.y;
    float intensityFactor = clamp((theta - light.cutoffs.y) / epsilon, 0.0, 1.0);
    
    // Diffuse calculation
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * albedo * light.colorAndIntensity.rgb * light.colorAndIntensity.a;
    
    // Specular calculation
    vec3 reflectDir = reflect(-lightDir, normal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), specularPower);
    vec3 specular = specularStrength * spec * light.colorAndIntensity.rgb * specIntensity * light.colorAndIntensity.a;
    
    // Attenuation calculation
    float attenuation = 1.0 / (1.0 + 0.07 * distance + 0.017 * (distance * distance));
    attenuation *= smoothstep(light.directionAndRadius.w, light.directionAndRadius.w * 0.75, distance);
    
    return (diffuse + specular) * attenuation * intensityFactor;
}

void main() {
    ivec2 texCoord = ivec2(gl_FragCoord.xy);
    vec3 fragPos = subpassLoad(gPosition).rgb;
    vec2 encodedNormal = subpassLoad(gNormal).rg;
    vec3 normal = octDecode(encodedNormal);
    vec4 albedoSpec = subpassLoad(gAlbedo);
    
    // Material properties
    vec3 albedo = pow(albedoSpec.rgb, vec3(2.2)); // sRGB to linear
    float specIntensity = albedoSpec.a;
    vec3 viewDir = normalize(camPos - fragPos);
    
    vec3 result = vec3(0.03) * albedo; // Ambient
    
    for(int i = 0; i < numLights; i++) {
        LightData light = lights[i];
        if(light.colorAndIntensity.a < MIN_LIGHT_THRESHOLD) continue;
        
        int lightType = int(light.positionAndType.w);
        switch(lightType) {
            case 0: // Directional
                result += CalculateDirectionalLight(light, normal, albedo, viewDir, specIntensity);
                break;
            case 1: // Point
                result += CalculatePointLight(light, fragPos, normal, albedo, viewDir, specIntensity);
                break;
            case 2: // Spot
                result += CalculateSpotLight(light, fragPos, normal, albedo, viewDir, specIntensity);
                break;
        }
    }
    
    // Tone mapping and gamma correction
    result = result / (result + vec3(1.0)); // Reinhard tone mapping
    result = pow(result, vec3(1.0/2.2)); // Gamma correction
    outColor = vec4(result, 1.0);
}
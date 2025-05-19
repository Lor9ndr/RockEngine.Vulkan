#version 460

#extension GL_ARB_separate_shader_objects : enable
#extension GL_KHR_vulkan_glsl : enable

// G-Buffer inputs
layout(input_attachment_index = 0, set = 2, binding = 0) uniform subpassInput gPosition;
layout(input_attachment_index = 1, set = 2, binding = 1) uniform subpassInput gNormal;
layout(input_attachment_index = 2, set = 2, binding = 2) uniform subpassInput gAlbedo;
layout(input_attachment_index = 3, set = 2, binding = 3) uniform subpassInput gMRA;

// Camera and lighting
layout(location = 0) in vec3 camPos;

// IBL Textures
layout(set = 3, binding = 0) uniform samplerCube irradianceMap;
layout(set = 3, binding = 1) uniform samplerCube prefilterMap;
layout(set = 3, binding = 2) uniform sampler2D brdfLUT;

struct LightData {
    vec4 positionAndType;
    vec4 directionAndRadius;
    vec4 colorAndIntensity;
    vec2 cutoffs;
};

layout(std430, set = 1, binding = 0) readonly buffer LightBuffer {
    LightData lights[];
};

layout(set = 1, binding = 1) uniform LightCount {
    int numLights;
};

layout(push_constant) uniform IBLParams {
    float exposure;      // [0.1 - 4.0] Typical HDR exposure range
    float envIntensity;  // [0.0 - 2.0] Environment map multiplier
    float aoStrength;    // [0.0 - 2.0] Ambient occlusion effect strength
} iblParams;

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;
const float MIN_ROUGHNESS = 0.045;
const float MIN_LIGHT_THRESHOLD = 0.001;
const float MAX_REFLECTION_LOD = 8.0;

// PBR Functions
vec3 fresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness) {
    return F0 + (max(vec3(1.0 - roughness * 0.95), F0) - F0) * pow(1.0 - cosTheta, 5.0);
}

vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    return a2 / (PI * (NdotH * NdotH * (a2 - 1.0) + 1.0) * a2);
}

float geometrySmith(float NdotV, float NdotL, float roughness) {
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    float ggxv = NdotV / (NdotV * (1.0 - k) + k);
    float ggxl = NdotL / (NdotL * (1.0 - k) + k);
    return ggxv * ggxl;
}



vec3 calculateDirectLighting(LightData light, vec3 albedo, float metallic, 
                           float roughness, vec3 N, vec3 V, vec3 F0, vec3 fragPos) {
    float lightType = light.positionAndType.w;
    vec3 L;
    float attenuation = 1.0;
    vec3 radiance = light.colorAndIntensity.rgb * light.colorAndIntensity.a;

    if(lightType == 0.0) { // Directional Light
        L = normalize(-light.directionAndRadius.xyz);
    } 
    else { // Point or Spot Light
        vec3 lightPos = light.positionAndType.xyz;
        vec3 toLight = lightPos - fragPos;
        float distance = length(toLight);
        L = normalize(toLight);
        
        // Distance attenuation
        float radius = light.directionAndRadius.w;
        float d = clamp(distance / radius, 0.0, 1.0);
        attenuation = 1.0 / (distance * distance + 1.0);
        attenuation *= (1.0 - d * d);
        
        if(lightType == 2.0) { // Spotlight
            float theta = dot(L, normalize(-light.directionAndRadius.xyz));
            float epsilon = light.cutoffs.x - light.cutoffs.y;
            float intensity = clamp((theta - light.cutoffs.y) / epsilon, 0.0, 1.0);
            attenuation *= intensity * intensity;
        }
    }

    // Common BRDF calculations
    vec3 H = normalize(V + L);
    float NDF = distributionGGX(N, H, roughness);
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float G = geometrySmith(NdotV, NdotL, roughness);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
    
    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);
    
    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0);
    vec3 specular = numerator / max(denominator, 0.001);
    
    return (kD * albedo / PI + specular) * radiance * attenuation * NdotL;
}


vec3 calculateIBL(vec3 N, vec3 V, vec3 F0, float roughness, float metallic, float ao, vec3 albedo, vec3 fragPos) {
    // Clamp roughness to avoid artifacts
    roughness = clamp(roughness, MIN_ROUGHNESS, 0.99);
    
    vec3 R = reflect(normalize(V), N); // V should point towards camera
    float NdotV = clamp(dot(N, V), 0.001, 1.0); // Avoid division by zero
    
    // Improved multi-scattering approximation
    vec3 F = fresnelSchlickRoughness(NdotV, F0, roughness);
    vec2 brdf = texture(brdfLUT, vec2(NdotV, roughness)).rg;
    
    // Energy-conserving diffuse calculation
    vec3 kD = (1.0 - F) * (1.0 - metallic);
    vec3 irradiance = texture(irradianceMap, N).rgb;
    vec3 diffuse = irradiance * albedo * kD;
    
    // Specular with mip level bias for better quality
    float mipBias = 1.0 - sqrt(roughness);
    float mipLevel = roughness * (MAX_REFLECTION_LOD - mipBias);
    vec3 prefiltered = textureLod(prefilterMap, R, mipLevel).rgb;
    // Add parallax correction:
    vec3 rayDir = R;
    float maxDistance = 100.0; // Match your environment size
    vec3 boxSize = vec3(maxDistance);
    vec3 firstPlaneIntersect = (boxSize - fragPos) / rayDir;
    vec3 secondPlaneIntersect = (-boxSize - fragPos) / rayDir;
    vec3 furthestPlane = max(firstPlaneIntersect, secondPlaneIntersect);
    float distance = min(min(furthestPlane.x, furthestPlane.y), furthestPlane.z);
    prefiltered = textureLod(prefilterMap, rayDir * distance, mipLevel).rgb;
    
    // Energy compensation with fallback
    float brdfY = max(brdf.y, 0.01); // Prevent division by zero
    vec3 energyCompensation = 1.0 + F * (1.0 / brdfY - 1.0);
    vec3 specular = prefiltered * (F * brdf.x + brdf.y) * energyCompensation;
    
    // Combine with artistic controls
    return (diffuse * iblParams.envIntensity + specular) * ao * iblParams.aoStrength;
}


vec3 tonemapFilmic(vec3 x) {
    vec3 X = max(vec3(0.0), x - 0.004);
    return (X * (6.2 * X + 0.5)) / (X * (6.2 * X + 1.7) + 0.06);
}

vec3 tonemapACES(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
     vec3 fragPos = subpassLoad(gPosition).rgb;
    // With proper octahedral decoding:
    vec2 encodedNormal = subpassLoad(gNormal).rg * 2.0 - 1.0;
    vec3 N;
    N.z = 1.0 - abs(encodedNormal.x) - abs(encodedNormal.y);
    N.xy = encodedNormal.xy;
    N = normalize(N);

    vec4 albedoData = subpassLoad(gAlbedo);
    vec4 mra = subpassLoad(gMRA);

    // Material properties from G-Buffer
    vec3 albedo = pow(albedoData.rgb, vec3(2.2));
    float metallic = clamp(mra.r, 0.0, 1.0);
    float roughness = clamp(mra.g, MIN_ROUGHNESS, 1.0);
    float ao = mix(1.0, clamp(mra.b, 0.0, 1.0), iblParams.aoStrength);

    vec3 V = normalize(camPos - fragPos);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    // Lighting calculations
    vec3 ambient = calculateIBL(N, V, F0, roughness, metallic, ao, albedo, fragPos);
    vec3 Lo = vec3(0.0);
    
    for(int i = 0; i < numLights; ++i) {
        if(lights[i].colorAndIntensity.a < MIN_LIGHT_THRESHOLD) continue;
        Lo += calculateDirectLighting(lights[i], albedo, metallic, roughness, N, V, F0, fragPos);
    }

    // HDR and tone mapping
    vec3 color = ambient + Lo;
    color *= iblParams.exposure * 2.0; // Scale for HDR range
    color = tonemapACES(color);
    color = pow(color, vec3(1.0/2.2));
    
    outColor = vec4(color, 1.0);
}
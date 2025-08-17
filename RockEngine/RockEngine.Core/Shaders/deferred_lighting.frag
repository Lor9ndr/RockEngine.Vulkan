#version 460
#extension GL_ARB_separate_shader_objects : enable
#extension GL_KHR_vulkan_glsl : enable

layout(input_attachment_index = 0, set = 2, binding = 0) uniform subpassInput gPosition;
layout(input_attachment_index = 1, set = 2, binding = 1) uniform subpassInput gNormal;
layout(input_attachment_index = 2, set = 2, binding = 2) uniform subpassInput gAlbedo;
layout(input_attachment_index = 3, set = 2, binding = 3) uniform subpassInput gMRA;
layout(input_attachment_index = 4, set = 2, binding = 4) uniform subpassInput gObjectID;

layout(location = 0) in vec3 camPos;

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
const float GAMMA = 2.2;
const float ENV_IOR = 1.5; // Environment index of refraction (1.5 for common dielectrics)

vec3 fresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness) {
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float denom = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom);
}

float geometrySchlickGGX(float Ndot, float roughness) {
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    return Ndot / (Ndot * (1.0 - k) + k);
}

float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    return geometrySchlickGGX(NdotV, roughness) * geometrySchlickGGX(NdotL, roughness);
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
        float dist = length(toLight);
        float radius = light.directionAndRadius.w;

        // Skip fragments beyond light radius
        if(dist > radius) return vec3(0.0);
        
        L = toLight / dist;
        
        // Physically based attenuation with smooth cutoff
        float dist2 = dist * dist;
        float atten = 1.0 / (dist2 + 1e-6);
        float fade = pow(clamp(1.0 - pow(dist / radius, 4.0), 0.0, 1.0), 2.0);
        attenuation = atten * fade;
        
        if(lightType == 2.0) { // Spotlight
            vec3 lightDir = normalize(-light.directionAndRadius.xyz);
            float theta = dot(L, lightDir);
            float innerCutoff = light.cutoffs.x;
            float outerCutoff = light.cutoffs.y;
            float epsilon = innerCutoff - outerCutoff;
            float intensity = clamp((theta - outerCutoff) / epsilon, 0.0, 1.0);
            attenuation *= intensity * intensity; // Quadratic falloff for smoother edges
        }
    }

    // Skip calculations for lights behind the surface
    float NdotL = max(dot(N, L), 0.0);
    if(NdotL <= 0.0) return vec3(0);
    
    // BRDF calculations
    vec3 H = normalize(V + L);
    float NdotV = max(dot(N, V), 0.0001);
    
    // Cook-Torrance BRDF
    float NDF = distributionGGX(N, H, roughness);
    float G = geometrySmith(N, V, L, roughness);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);
    
    vec3 kS = F;
    vec3 kD = (vec3(1.0) - kS) * (1.0 - metallic);
    
    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL;
    vec3 specular = numerator / max(denominator, 0.001);
    
    return (kD * albedo / PI + specular) * radiance * attenuation * NdotL;
}

vec3 calculateIBL(vec3 N, vec3 V, vec3 F0, float roughness, float metallic, float ao, vec3 albedo) {
    roughness = clamp(roughness, MIN_ROUGHNESS, 0.99);
    float NdotV = clamp(dot(N, V), 0.001, 1.0);
    
    // Specular IBL
    vec3 R = reflect(-V, N);
    vec3 F = fresnelSchlickRoughness(NdotV, F0, roughness);
    vec2 brdf = textureLod(brdfLUT, vec2(NdotV, roughness), 0.0).rg;
    vec3 prefiltered = textureLod(prefilterMap, R, roughness * MAX_REFLECTION_LOD).rgb;
    vec3 specular = prefiltered * (F * brdf.x + brdf.y);
    
    // Diffuse IBL
    vec3 kD = (1.0 - F) * (1.0 - metallic);
    vec3 irradiance = texture(irradianceMap, N).rgb;
    vec3 diffuse = irradiance * albedo;
    
    // Ambient composition
    return (kD * diffuse + specular) * ao * iblParams.envIntensity;
}

vec3 tonemapACES(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    vec3 numerator = x * (a * x + b);
    vec3 denominator = x * (c * x + d) + e;
    return clamp(numerator / max(denominator, vec3(1e-5)), 0.0, 1.0);
}

vec3 decodeNormal(vec2 enc) {
    vec3 n;
    n.z = 1.0 - abs(enc.x) - abs(enc.y);
    n.xy = n.z >= 0.0 ? enc.xy : sign(enc.xy) * (vec2(1.0) - abs(enc.yx));
    return normalize(n);
}

void main() {
    vec3 fragPos = subpassLoad(gPosition).rgb;
    vec2 encodedNormal = subpassLoad(gNormal).rg * 2.0 - 1.0;
    vec3 N = decodeNormal(encodedNormal);
    vec4 albedoData = subpassLoad(gAlbedo);
    vec4 mra = subpassLoad(gMRA);

    // Use linear-space albedo (remove gamma correction)
    vec3 albedo = albedoData.rgb;
    float metallic = clamp(mra.r, 0.0, 1.0);
    float roughness = clamp(mra.g, MIN_ROUGHNESS, 1.0);
    float ao = mix(1.0, mra.b, iblParams.aoStrength);

    vec3 V = normalize(camPos - fragPos);
    
    // Base reflectivity (0.04 for dielectrics, albedo for metals)
    vec3 F0 = mix(vec3(pow((ENV_IOR - 1.0) / (ENV_IOR + 1.0), 2.0)), albedo, metallic);

    // Ambient lighting (IBL)
    vec3 ambient = calculateIBL(N, V, F0, roughness, metallic, ao, albedo);
    vec3 Lo = vec3(0.0);
    
    // Process active lights
    int lightCount = min(numLights, 256);
    for(int i = 0; i < lightCount; i++) {
        if(lights[i].colorAndIntensity.a < MIN_LIGHT_THRESHOLD) continue;
        Lo += calculateDirectLighting(lights[i], albedo, metallic, roughness, N, V, F0, fragPos);
    }

    // Combine lighting
    vec3 color = ambient + Lo;
    
    // HDR tonemapping and gamma correction
    color *= iblParams.exposure;
    color = tonemapACES(color);
    color = pow(color, vec3(1.0 / GAMMA));
    
    outColor = vec4(color, 1.0);
}
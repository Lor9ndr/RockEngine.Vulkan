#version 460
#extension GL_ARB_separate_shader_objects : enable
#extension GL_KHR_vulkan_glsl : enable
#extension GL_EXT_scalar_block_layout :enable

layout(input_attachment_index = 0, set = 2, binding = 0) uniform subpassInput gPosition;
layout(input_attachment_index = 1, set = 2, binding = 1) uniform subpassInput gNormal;
layout(input_attachment_index = 2, set = 2, binding = 2) uniform subpassInput gAlbedo;
layout(input_attachment_index = 3, set = 2, binding = 3) uniform subpassInput gMRA;

layout(location = 0) in vec3 camPos;

layout(set = 3, binding = 0) uniform samplerCube irradianceMap;
layout(set = 3, binding = 1) uniform samplerCube prefilterMap;
layout(set = 3, binding = 2) uniform sampler2D brdfLUT;

// Shadow mapping uniforms
layout(set = 4, binding = 0) uniform sampler2DArray shadowMaps;
layout(set = 4, binding = 1) uniform samplerCubeArray pointShadowMaps;

// CSM data for directional lights
struct CSMData {
    mat4 cascadeMatrices[4];
    vec4 cascadeSplits;
    vec4 csmParams;
    mat4 viewMatrix;
};

layout(scalar, set = 5, binding = 0) uniform CSMDataBuffer {
    CSMData csmData;
};

struct LightData {
    vec4 positionAndType;
    vec4 directionAndRadius;
    vec4 colorAndIntensity;
    vec4 shadowParams;
    mat4 shadowMatrix;
    vec2 cutoffs;

};

layout(scalar, set = 1, binding = 1) readonly buffer LightBuffer {
    LightData lights[];
};

layout(set = 1, binding = 0) uniform LightCount {
    int numLights;
};

layout(push_constant) uniform IBLParams {
    float exposure;      // [0.1 - 4.0] Typical HDR exposure range
    float envIntensity;  // [0.0 - 2.0] Environment map multiplier
    float aoStrength;    // [0.0 - 2.0] Ambient occlusion effect strength
    float gamma;         // [1.8 - 2.4] Gamma correction
    float envRotation;   // [0.0 - 2*PI] Environment map rotation
} iblParams;

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265359;
const float MIN_ROUGHNESS = 0.045;
const float MIN_LIGHT_THRESHOLD = 0.001;
const float MAX_REFLECTION_LOD = 8.0;
const float ENV_IOR = 1.5;
const float PCF_RADIUS = 1.5;

// PCF constants
const int PCF_SAMPLES = 16;
const vec2 POISSON_DISK[16] = vec2[](
    vec2(-0.94201624, -0.39906216), vec2(0.94558609, -0.76890725),
    vec2(-0.094184101, -0.92938870), vec2(0.34495938, 0.29387760),
    vec2(-0.91588581, 0.45771432), vec2(-0.81544232, -0.87912464),
    vec2(-0.38277543, 0.27676845), vec2(0.97484398, 0.75648379),
    vec2(0.44323325, -0.97511554), vec2(0.53742981, -0.47373420),
    vec2(-0.26496911, -0.41893023), vec2(0.79197514, 0.19090188),
    vec2(-0.24188840, 0.99706507), vec2(-0.81409955, 0.91437590),
    vec2(0.19984126, 0.78641367), vec2(0.14383161, -0.14100790)
);

// Cube map sampling vectors for point shadows
const vec3 CUBE_OFFSETS[20] = vec3[](
    vec3( 1,  1,  1), vec3( 1, -1,  1), vec3(-1, -1,  1), vec3(-1,  1,  1),
    vec3( 1,  1, -1), vec3( 1, -1, -1), vec3(-1, -1, -1), vec3(-1,  1, -1),
    vec3( 1,  1,  0), vec3( 1, -1,  0), vec3(-1, -1,  0), vec3(-1,  1,  0),
    vec3( 1,  0,  1), vec3(-1,  0,  1), vec3( 1,  0, -1), vec3(-1,  0, -1),
    vec3( 0,  1,  1), vec3( 0, -1,  1), vec3( 0, -1, -1), vec3( 0,  1, -1)
);

// Cascade visualization colors
vec3 CASCADE_COLORS[4] = vec3[](
    vec3(1.0, 0.0, 0.0), // Red - first cascade
    vec3(0.0, 1.0, 0.0), // Green - second cascade
    vec3(0.0, 0.0, 1.0), // Blue - third cascade
    vec3(1.0, 1.0, 0.0)  // Yellow - fourth cascade
);

// Improved Fresnel with better roughness handling
vec3 fresnelSchlickRoughness(float cosTheta, vec3 F0, float roughness) {
    return F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

vec3 fresnelSchlick(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

// Improved GGX distribution
float distributionGGX(vec3 N, vec3 H, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    return a2 / max(denom, 0.0000001);
}

// Smith geometry function
float geometrySchlickGGX(float NdotV, float roughness) {
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness) {
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx1 = geometrySchlickGGX(NdotV, roughness);
    float ggx2 = geometrySchlickGGX(NdotL, roughness);
    return ggx1 * ggx2;
}

// Environment rotation
mat3 rotationMatrix(vec3 axis, float angle) {
    axis = normalize(axis);
    float s = sin(angle);
    float c = cos(angle);
    float oc = 1.0 - c;
    
    return mat3(
        oc * axis.x * axis.x + c,           oc * axis.x * axis.y - axis.z * s,  oc * axis.z * axis.x + axis.y * s,
        oc * axis.x * axis.y + axis.z * s,  oc * axis.y * axis.y + c,           oc * axis.y * axis.z - axis.x * s,
        oc * axis.z * axis.x - axis.y * s,  oc * axis.y * axis.z + axis.x * s,  oc * axis.z * axis.z + c
    );
}

// Function to select which cascade to use
int selectCascade(vec3 fragPos, vec3 viewPos, mat4 viewMatrix) {
    // Transform fragment to view space for proper depth calculation
    vec4 fragPosView = viewMatrix * vec4(fragPos, 1.0);
    float viewDepth = -fragPosView.z; // Negative Z in view space is forward
    
    // Find the appropriate cascade
    for (int i = 0; i < 3; ++i) {
        if (viewDepth < csmData.cascadeSplits[i]) {
            return i;
        }
    }
    return 3;
}

float calculateCSMShadow(vec3 fragPos, vec3 normal, vec3 lightDir, LightData light) {
    if (light.shadowParams.z < 0.5) return 0.0;
    
    int shadowIndex = int(light.shadowParams.w);
    int cascadeIndex = selectCascade(fragPos, camPos, csmData.viewMatrix);
    
    // Improved bias calculation
    float baseBias = light.shadowParams.x;
    float normalBias = baseBias * tan(acos(clamp(dot(normal, lightDir), 0.0, 1.0)));
    float bias = max(baseBias * 0.5, normalBias);
    
    // Get the correct cascade matrix
    mat4 shadowMatrix;
    if (cascadeIndex == 0) shadowMatrix = csmData.cascadeMatrices[0];
    else if (cascadeIndex == 1) shadowMatrix = csmData.cascadeMatrices[1];
    else if (cascadeIndex == 2) shadowMatrix = csmData.cascadeMatrices[2];
    else shadowMatrix = csmData.cascadeMatrices[3];
    
    // Apply normal offset
    vec3 normalOffset = normal * (bias * 2.0);
    vec4 shadowCoord = shadowMatrix * vec4(fragPos + normalOffset, 1.0);
    
    // Perspective divide
    shadowCoord.xyz /= shadowCoord.w;
    
    // Transform from [-1,1] to [0,1] for texture sampling
    shadowCoord.xyz = shadowCoord.xyz * 0.5 + 0.5;
    
    // Early out if outside shadow map with small margin
    if (any(lessThan(shadowCoord.xy, vec2(0.01))) || 
        any(greaterThan(shadowCoord.xy, vec2(0.99))) ||
        shadowCoord.z > 1.0) {
        return 0.0;
    }
    
    // Clamp to avoid edge artifacts
    shadowCoord.xy = clamp(shadowCoord.xy, 0.01, 0.99);
    
    // Calculate layer
    int layer = shadowIndex * 4 + cascadeIndex;
    
    // Improved PCF
    float shadow = 0.0;
    vec2 texelSize = 1.0 / vec2(textureSize(shadowMaps, 0));
    float radius = 1.0;
    
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            vec2 sampleCoord = shadowCoord.xy + vec2(x, y) * texelSize * radius;
            float closestDepth = texture(shadowMaps, vec3(sampleCoord, layer)).r;
            
            // Smooth depth comparison
            float depthDiff = shadowCoord.z - bias - closestDepth;
            shadow += (depthDiff > 0.0) ? smoothstep(0.0, 0.001, depthDiff) : 0.0;
        }
    }
    
    shadow /= 9.0; // 3x3 kernel
    return shadow * light.shadowParams.y;
}

// Basic PCF for single cascade directional lights (fallback)
float calculateDirectionalShadow(vec3 fragPos, vec3 normal, vec3 lightDir, LightData light) {
    if (light.shadowParams.z < 0.5) return 0.0;
    
    int shadowIndex = int(light.shadowParams.w);
    float bias = max(light.shadowParams.x * 0.1, light.shadowParams.x * (1.0 - dot(normal, lightDir)));
    
    // Transform to shadow space
    vec4 shadowCoord = light.shadowMatrix * vec4(fragPos, 1.0);
    shadowCoord.xyz /= shadowCoord.w;
    shadowCoord.xyz = shadowCoord.xyz * 0.5 + 0.5;
    
    // Early out if outside shadow map
    if (shadowCoord.x < 0.0 || shadowCoord.x > 1.0 || 
        shadowCoord.y < 0.0 || shadowCoord.y > 1.0 || 
        shadowCoord.z > 1.0) {
        return 0.0;
    }
    
    // PCF filtering
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMaps, 0).xy;
    
    for (int i = 0; i < PCF_SAMPLES; i++) {
        vec2 sampleCoord = shadowCoord.xy + POISSON_DISK[i] * texelSize * PCF_RADIUS;
        float closestDepth = texture(shadowMaps, vec3(sampleCoord, shadowIndex)).r;
        shadow += (shadowCoord.z - bias) > closestDepth ? 1.0 : 0.0;
    }
    
    shadow /= float(PCF_SAMPLES);
    return shadow * light.shadowParams.y;
}

// PCF for spot lights
float calculateSpotShadow(vec3 fragPos, vec3 normal, vec3 lightDir, LightData light) {
    if (light.shadowParams.z < 0.5) return 0.0;
    
    int shadowIndex = int(light.shadowParams.w);
    
    // Better bias calculation for spot lights
    float baseBias = light.shadowParams.x;
    float normalBias = baseBias * tan(acos(clamp(dot(normal, lightDir), 0.0, 1.0)));
    float bias = max(baseBias * 0.3, normalBias);
    
    // Transform to light space
    vec4 shadowCoord = light.shadowMatrix * vec4(fragPos, 1.0);
    
    // Proper perspective divide for spot lights (perspective projection)
    shadowCoord.xyz /= shadowCoord.w;
    
    // Transform from [-1,1] to [0,1] for texture sampling
    shadowCoord.xyz = shadowCoord.xyz * 0.5 + 0.5;
    
    if (any(lessThan(shadowCoord.xy, vec2(0.01))) || 
        any(greaterThan(shadowCoord.xy, vec2(0.99))) ||
        shadowCoord.z < 0.0 || shadowCoord.z > 1.0) {
        return 0.0;
    }
    
    // Apply bias before comparison
    float currentDepth = shadowCoord.z - bias;
    
    // Clamp to avoid edge artifacts
    shadowCoord.xy = clamp(shadowCoord.xy, 0.01, 0.99);
    
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMaps, 0).xy;
    
    // Use fewer samples for distant fragments
    float viewDistance = length(camPos - fragPos);
    int samples = (viewDistance > 25.0) ? 9 : 16;
    float radius = (viewDistance > 25.0) ? 1.0 : 1.5;
    
    if (samples == 9) {
        // 3x3 kernel
        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                vec2 sampleCoord = shadowCoord.xy + vec2(x, y) * texelSize * radius;
                float closestDepth = texture(shadowMaps, vec3(sampleCoord, shadowIndex)).r;
                shadow += (currentDepth > closestDepth) ? 1.0 : 0.0;
            }
        }
        shadow /= 9.0;
    } else {
        // 16 Poisson samples
        for (int i = 0; i < PCF_SAMPLES; i++) {
            vec2 sampleCoord = shadowCoord.xy + POISSON_DISK[i] * texelSize * radius;
            float closestDepth = texture(shadowMaps, vec3(sampleCoord, shadowIndex)).r;
            shadow += (currentDepth > closestDepth) ? 1.0 : 0.0;
        }
        shadow /= float(PCF_SAMPLES);
    }
    
    return shadow * light.shadowParams.y;
}

// FIXED: Optimized point shadow with proper cube map sampling
float calculatePointShadow(vec3 fragPos, vec3 normal, vec3 lightPos, LightData light) {
    if (light.shadowParams.z < 0.5) return 0.0;
    
    int shadowIndex = int(light.shadowParams.w);
    vec3 fragToLight = fragPos - lightPos;
    float currentDepth = length(fragToLight);
    
    // Early out if beyond light radius
    if (currentDepth > light.directionAndRadius.w) {
        return 0.0;
    }
    
    vec3 lightDir = normalize(fragToLight);
    
    // Calculate bias based on normal and light direction
    float bias = max(light.shadowParams.x * 0.05, light.shadowParams.x * (1.0 - dot(normal, -lightDir)));
    
    // Normalize current depth to [0, 1] range
    float currentDepthNormalized = currentDepth / light.directionAndRadius.w;
    
    // Use fewer samples for distant fragments
    float viewDistance = length(camPos - fragPos);
    int samples = (viewDistance > 25.0) ? 8 : 16;
    
    float shadow = 0.0;
    float diskRadius = (0.015 + 0.02 * (1.0 - dot(normal, -lightDir))) * (1.0 / (currentDepthNormalized + 0.1));
    
    for (int i = 0; i < samples; i++) {
        // Sample in the direction from light to fragment
        vec3 sampleOffset = normalize(fragToLight + CUBE_OFFSETS[i] * diskRadius);
        float closestDepth = texture(pointShadowMaps, vec4(sampleOffset, shadowIndex)).r;
        
        // Compare depths
        if (currentDepthNormalized - bias > closestDepth) {
            shadow += 1.0;
        }
    }
    
    shadow /= float(samples);
    return shadow * light.shadowParams.y;
}

// Enhanced direct lighting calculation
vec3 calculateDirectLighting(LightData light, vec3 albedo, float metallic, 
                           float roughness, vec3 N, vec3 V, vec3 F0, vec3 fragPos, vec3 normal) {
    float lightType = light.positionAndType.w;
    vec3 L;
    float attenuation = 1.0;
    vec3 radiance = light.colorAndIntensity.rgb * light.colorAndIntensity.a;
    float shadow = 0.0;

    if(lightType == 0.0) { // Directional Light
        L = normalize(-light.directionAndRadius.xyz);
        
        // Always use CSM for directional lights when available
        if (csmData.cascadeSplits.x > 0.0) {
            shadow = calculateCSMShadow(fragPos, normal, L, light);
        } else {
            // Fallback to single cascade
            shadow = calculateDirectionalShadow(fragPos, normal, L, light);
        }
        
        // Directional lights have constant attenuation
        attenuation = 1.0;
    } 
    else { // Point or Spot Light
        vec3 lightPos = light.positionAndType.xyz;
        vec3 toLight = lightPos - fragPos;
        float dist = length(toLight);
        float radius = light.directionAndRadius.w;

        if(dist > radius) return vec3(0.0);
        
        L = toLight / dist;
        
        // Improved attenuation with inverse square law
        float dist2 = dist * dist;
        float radius2 = radius * radius;
        float atten = 1.0 / (dist2 + 1e-6);
        float fade = pow(clamp(1.0 - (dist2 / radius2), 0.0, 1.0), 2.0);
        attenuation = atten * fade;
        
        if(lightType == 2.0) { // Spotlight
            vec3 lightDir = normalize(-light.directionAndRadius.xyz);
            float theta = dot(L, lightDir);
            float innerCutoff = light.cutoffs.x;
            float outerCutoff = light.cutoffs.y;
            float epsilon = innerCutoff - outerCutoff;
            float intensity = clamp((theta - outerCutoff) / epsilon, 0.0, 1.0);
            attenuation *= intensity * intensity;
            
            shadow = calculateSpotShadow(fragPos, normal, L, light);
        } else { // Point light
            shadow = calculatePointShadow(fragPos, normal, lightPos, light);
        }
    }

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
    
    float shadowFactor = 1.0 - shadow;
    
    return (kD * albedo / PI + specular) * radiance * attenuation * NdotL * shadowFactor;
}

// Debug function to visualize cascades
vec3 debugCascadeVisualization(vec3 fragPos, vec3 viewPos, vec3 originalColor, mat4 viewMatrix) {
    int cascadeIndex = selectCascade(fragPos, viewPos, viewMatrix);
    
    // More subtle coloring that shows cascade boundaries
    vec3 cascadeColor = CASCADE_COLORS[cascadeIndex] * 0.4;
    
    // Add edge detection for cascade boundaries
    float viewDepth = distance(fragPos, viewPos);
    float nextSplit = (cascadeIndex < 3) ? csmData.cascadeSplits[cascadeIndex] : 1000.0;
    float border = 1.0 - smoothstep(nextSplit - 5.0, nextSplit, viewDepth);
    
    return mix(originalColor, cascadeColor, 0.2 + border * 0.3);
}

// FIXED: Enhanced IBL calculation with proper energy conservation
vec3 calculateIBL(vec3 N, vec3 V, vec3 F0, float roughness, float metallic, float ao, vec3 albedo) {
    roughness = clamp(roughness, MIN_ROUGHNESS, 0.99);
    float NdotV = clamp(dot(N, V), 0.001, 1.0);
    
    // Apply environment rotation
    mat3 rotMatrix = rotationMatrix(vec3(0.0, 1.0, 0.0), iblParams.envRotation);
    vec3 N_rot = rotMatrix * N;
    vec3 R_rot = rotMatrix * reflect(-V, N);
    
    // Specular IBL - improved LOD calculation
    vec3 F = fresnelSchlickRoughness(NdotV, F0, roughness);
    
    // Sample prefiltered environment with proper LOD
    float lod = roughness * (MAX_REFLECTION_LOD - 1.0);
    vec3 prefilteredColor = textureLod(prefilterMap, R_rot, lod).rgb;
    
    // Sample BRDF LUT
    vec2 brdfSample = texture(brdfLUT, vec2(NdotV, roughness)).rg;
    vec3 specularIBL = prefilteredColor * (F * brdfSample.x + brdfSample.y);
    
    // FIXED: Diffuse IBL with proper energy conservation
    vec3 kD = (1.0 - F) * (1.0 - metallic);
    vec3 irradiance = texture(irradianceMap, N_rot).rgb;
    
    // Apply albedo to diffuse IBL
    vec3 diffuseIBL = kD * irradiance * albedo;
    
    // Combine with proper intensity and AO
    vec3 ambient = (diffuseIBL + specularIBL) * ao;
    
    // Apply environment intensity with non-linear response for more artistic control
    float intensity = iblParams.envIntensity;
    ambient *= intensity * (0.8 + 0.2 * intensity); // S-curve-like response
    
    return ambient;
}

// Improved tonemapping with better highlight handling
vec3 tonemapACES(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

// Alternative tonemapping operator
vec3 tonemapFilmic(vec3 x) {
    vec3 X = max(vec3(0.0), x - 0.004);
    vec3 result = (X * (6.2 * X + 0.5)) / (X * (6.2 * X + 1.7) + 0.06);
    return pow(result, vec3(2.2));
}

vec3 decodeNormal(vec2 enc) {
    vec3 n;
    n.z = 1.0 - abs(enc.x) - abs(enc.y);
    n.xy = n.z >= 0.0 ? enc.xy : sign(enc.xy) * (vec2(1.0) - abs(enc.yx));
    return normalize(n);
}

void main() {
    // Sample G-buffer
    vec3 fragPos = subpassLoad(gPosition).rgb;
    vec2 encodedNormal = subpassLoad(gNormal).rg * 2.0 - 1.0;
    vec3 N = decodeNormal(encodedNormal);
    vec4 albedoData = subpassLoad(gAlbedo);
    vec4 mra = subpassLoad(gMRA);

    vec3 albedo = albedoData.rgb;
    float metallic = clamp(mra.r, 0.0, 1.0);
    float roughness = clamp(mra.g, MIN_ROUGHNESS, 1.0);
    float ao = mix(1.0, mra.b, iblParams.aoStrength);

    vec3 V = normalize(camPos - fragPos);
    
    // Base reflectivity
    vec3 F0 = mix(vec3(pow((ENV_IOR - 1.0) / (ENV_IOR + 1.0), 2.0)), albedo, metallic);

    // Enhanced IBL lighting
    vec3 ambient = calculateIBL(N, V, F0, roughness, metallic, ao, albedo);
    vec3 Lo = vec3(0.0);
    
    // Direct lighting
    int lightCount = min(numLights, 100);
    for(int i = 0; i < lightCount; i++) {
        if(lights[i].colorAndIntensity.a < MIN_LIGHT_THRESHOLD) continue;
        Lo += calculateDirectLighting(lights[i], albedo, metallic, roughness, N, V, F0, fragPos, N);
    }

    // Combine lighting with exposure
    vec3 color = ambient + Lo;
    color *= iblParams.exposure;
    
    // Debug cascade visualization (uncomment to debug)
    // color = debugCascadeVisualization(fragPos, camPos, color, csmData.viewMatrix);
    
    // Tonemapping
    color = tonemapACES(color);
    
    // Gamma correction with adjustable gamma
    color = pow(color, vec3(1.0 / iblParams.gamma));
    
    outColor = vec4(color, 1.0);
}
#version 450
#include "Include/common.glsl"

layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {
    GlobalUBO ubo;
};

layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec2 vTexCoord;
layout(location = 3) in mat3 vTBN;

layout(location = 0) out vec4 gNormal;   // RG=normal, B=roughness, A=metallic
layout(location = 1) out vec4 gAlbedo;

[MATERIAL]
{
    Texture2D Albedo;
    Texture2D Normal;
    Texture2D MRA;
}

vec2 octEncodeFullSphere(vec3 n) {
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    if (n.z < 0.0) {
        n.xy = (1.0 - abs(n.yx)) * sign(n.xy);
    }
    return n.xy * 0.5 + 0.5;
}

void main() {
    vec4 albedo = sampleAlbedo(vTexCoord);
    if (albedo.a < 0.01) discard;

    vec4 mra = sampleMRA(vTexCoord);
    vec3 tangentNormal = sampleNormal(vTexCoord).xyz * 2.0 - 1.0;
    vec3 worldNormal = normalize(vTBN * tangentNormal);   

    gAlbedo = vec4(albedo.rgb, mra.b);
    gNormal = vec4(octEncodeFullSphere(worldNormal), mra.g, mra.r);
}
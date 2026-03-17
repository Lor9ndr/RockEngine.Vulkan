#version 450
#include "Include/common.glsl"

#extension GL_KHR_vulkan_glsl:enable


layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec2 vTexCoord;
layout(location = 3) in mat3 vTBN;

layout(location = 0) out vec3 gPosition;
layout(location = 1) out vec3 gNormal;
layout(location = 2) out vec4 gAlbedo;
layout(location = 3) out vec4 gMRA;

[MATERIAL]
{
    Texture2D Albedo;
    Texture2D Normal;
    Texture2D MRA;
}

vec2 octEncode(vec3 n) {
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = n.z >= 0.0 ? n.xy : (1.0 - abs(n.yx)) * sign(n.xy);
    return n.xy * 0.5 + 0.5 + 0.5/32768.0; // Add small offset
}

void main() 
{
    gPosition = vWorldPos;
    gAlbedo = sampleAlbedo(vTexCoord);
    if (gAlbedo.a < 0.01) {
        discard;
    }
    vec3 tangentNormal = sampleNormal(vTexCoord).xyz * 2.0 - 1.0;
    vec4 mra = sampleMRA(vTexCoord);
    vec3 worldNormal = normalize(vTBN * tangentNormal);
    gNormal.xy = octEncode(worldNormal); // Store compressed normal
    gMRA = mra;

    //gEmissive = texture(uEmissive, vTexCoord);
}


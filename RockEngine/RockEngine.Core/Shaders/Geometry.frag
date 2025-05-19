#version 450
#extension GL_KHR_vulkan_glsl:enable
layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec2 vTexCoord;
layout(location = 3) in mat3 vTBN;

layout(location = 0) out vec3 gPosition;
layout(location = 1) out vec3 gNormal;
layout(location = 2) out vec4 gAlbedo;
layout(location = 3) out vec4 gMRA;

layout(set = 2, binding = 0) uniform sampler2D uAlbedo;
layout(set = 2, binding = 1) uniform sampler2D uNormalMap;
layout(set = 2, binding = 2) uniform sampler2D uMRA;

vec2 octEncode(vec3 n) {
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = n.z >= 0.0 ? n.xy : (1.0 - abs(n.yx)) * sign(n.xy);
    return n.xy * 0.5 + 0.5 + 0.5/32768.0; // Add small offset
}

void main() {
    gPosition = vWorldPos;
   
    vec3 tangentNormal = texture(uNormalMap, vTexCoord).xyz * 2.0 - 1.0;
    
    // Transform to world space
    vec3 worldNormal = normalize(vTBN * tangentNormal);
    gNormal.xy = octEncode(worldNormal); // Store compressed normal
    gAlbedo = texture(uAlbedo, vTexCoord);
    gMRA = texture(uMRA, vTexCoord);
    //gEmissive = texture(uEmissive, vTexCoord);
}


#version 450
#extension GL_KHR_vulkan_glsl:enable
layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec2 vTexCoord;
layout(location = 3) in mat3 vTBN;

layout(location = 0) out vec3 gPosition;
layout(location = 1) out vec3 gNormal;
layout(location = 2) out vec4 gAlbedo;

layout(set = 2, binding = 0) uniform sampler2D uAlbedo;
layout(set = 2, binding = 1) uniform sampler2D uNormalMap;

void main() {
    gPosition = vWorldPos;
   
    vec3 tangentNormal = texture(uNormalMap, vTexCoord).xyz * 2.0 - 1.0;
    
    // Transform to world space
    gNormal = normalize(vTBN * tangentNormal);
    gAlbedo = texture(uAlbedo, vTexCoord);
}
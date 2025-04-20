#version 460
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec3 aTangent;
layout(location = 4) in vec3 aBitangent;


layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};


layout(set = 0, binding = 0) uniform GlobalUbo {
    mat4 viewProj;
    vec3 camPos;
} ubo;

layout(location = 0) out vec3 vWorldPos;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec2 vTexCoord;

layout(location = 3) out mat3 vTBN;

void main() {
    mat4 model = models[gl_BaseInstance];
    vec4 worldPos =  model * vec4(aPosition, 1.0);
    gl_Position = ubo.viewProj * worldPos;
    vWorldPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(model))) * aNormal;
    vTexCoord = aTexCoord;
    vec3 T = normalize(mat3(model) * aTangent);
    vec3 B = normalize(mat3(model) * aBitangent);
    vec3 N = normalize(mat3(model) * aNormal);
    vTBN = mat3(T, B, N);
}
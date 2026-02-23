#version 460
#extension GL_KHR_vulkan_glsl:enable
#include "Include/common.glsl"


layout(location = 0) in vec3 aPosition;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};

layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {
   GlobalUBO ubo;
};


void main() {
    mat4 model = models[gl_BaseInstance];
    vec4 worldPos = model * vec4(aPosition, 1.0);
    gl_Position = ubo.viewProj * worldPos;
}
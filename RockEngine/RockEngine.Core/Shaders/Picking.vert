#version 460
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec3 aPosition;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};

layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {
    mat4 viewProj;
    vec3 camPos;
} ubo;


void main() {
    mat4 model = models[gl_BaseInstance];
    vec4 worldPos = model * vec4(aPosition, 1.0);
    gl_Position = ubo.viewProj * worldPos;
}
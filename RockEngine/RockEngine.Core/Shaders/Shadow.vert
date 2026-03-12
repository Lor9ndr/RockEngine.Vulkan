#version 460
#extension GL_ARB_separate_shader_objects : enable
#extension GL_KHR_vulkan_glsl : enable

layout(location = 0) in vec3 aPosition;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};

layout(push_constant) uniform ShadowPC {
    mat4 shadowMatrix;
} pc;

void main() {
    mat4 model = models[gl_BaseInstance];
    
    // Transform vertex through model matrix then shadow matrix
    vec4 worldPos = model * vec4(aPosition, 1.0);
    gl_Position = pc.shadowMatrix * worldPos;
}
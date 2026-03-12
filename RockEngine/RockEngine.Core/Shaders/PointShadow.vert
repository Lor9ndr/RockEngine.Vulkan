#version 460
#extension GL_ARB_separate_shader_objects : enable

layout(location = 0) in vec3 aPosition;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};

layout(push_constant) uniform ShadowPC {
    vec4 lightPos;
    float farPlane;
    uint shadowIndex;
} pc;

layout(location = 0) out vec3 fragPos;

void main() {
    mat4 model = models[gl_BaseInstance];
    fragPos = vec3(model * vec4(aPosition, 1.0));
}
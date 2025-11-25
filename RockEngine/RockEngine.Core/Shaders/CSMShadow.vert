#version 460
#extension GL_ARB_separate_shader_objects : enable

layout(location = 0) in vec3 aPosition;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};

void main() {
    mat4 model = models[gl_BaseInstance];
    gl_Position = model * vec4(aPosition, 1.0);
}
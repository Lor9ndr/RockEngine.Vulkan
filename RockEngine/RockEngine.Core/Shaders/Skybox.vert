#version 450

// Input with explicit location
layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec3 aTangent;
layout(location = 4) in vec3 aBitangent;

// Uniform block
layout(set = 0, binding = 0) uniform GlobalData {
    mat4 viewProjection;
    vec3 position;
} globalData;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};

// Output with explicit location
layout(location = 0) out vec3 fragPos;

// Required for Vulkan
out gl_PerVertex { vec4 gl_Position; };

void main() {
    fragPos = inPosition;                // Pass position to fragment shader
    gl_Position = globalData.viewProjection * vec4(inPosition * 1000000, 1.0);
    gl_Position = gl_Position.xyww; // Ensure depth is 1.0 after perspective divide
}
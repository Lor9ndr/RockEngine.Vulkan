#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inTexCoords;

layout(location = 0) out vec4 fragPos;
layout(location = 1) out vec2 texCoords;
layout(location = 2) out vec3 normals;
layout(location = 3) out vec3 viewPos;

layout(set = 0, binding = 0) uniform CameraData
{
    mat4 viewProj;
    vec3 camViewPos;
};

layout(set = 1, binding = 0) uniform Model_Dynamic
{
    mat4 model;
};


void main() 
{
    gl_Position =  viewProj * model * vec4(inPosition, 1.0);
    fragPos = gl_Position;
    normals = inNormal;
    texCoords = inTexCoords;
    viewPos = camViewPos;
}
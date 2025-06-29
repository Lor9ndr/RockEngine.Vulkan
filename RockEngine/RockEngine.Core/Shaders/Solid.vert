#version 460


layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec3 aTangent;
layout(location = 4) in vec3 aBitangent;


layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {
    mat4 viewProj;
    vec3 camPos;
} ubo;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};



void main() 
{
    mat4 model = models[gl_BaseInstance];
    vec4 worldPos =  model * vec4(inPosition, 1.0);
    gl_Position = ubo.viewProj * worldPos;
}
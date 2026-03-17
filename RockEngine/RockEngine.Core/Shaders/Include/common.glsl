#ifndef COMMON_GLSL
#define COMMON_GLSL

#define MATERIAL_SET 2  

struct GlobalUBO
{
    mat4 viewProj;
    mat4 view;
    mat4 proj;
    mat4 invView;
    mat4 invProj;
    mat4 invViewProj;
    vec3 camPos;
    vec2 screenSize;
    float farClip;
};

#endif
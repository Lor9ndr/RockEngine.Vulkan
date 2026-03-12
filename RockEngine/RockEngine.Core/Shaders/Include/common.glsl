

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
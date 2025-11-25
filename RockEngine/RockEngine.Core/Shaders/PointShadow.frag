#version 460
#extension GL_ARB_separate_shader_objects : enable

layout(push_constant) uniform ShadowPC {
    vec3 lightPos;
    float farPlane;
    uint shadowIndex;
} pc;

layout(location = 1) in vec4 fragPosLightSpace;
layout(location = 2) in vec3 worldPos;

void main() {
    // Optimized distance calculation - use dot product instead of length
    vec3 toFragment = worldPos - pc.lightPos;
    float lightDistance = dot(toFragment, toFragment); // squared distance
    
    // Compare squared distances to avoid sqrt
    float farPlaneSq = pc.farPlane * pc.farPlane;
    
    // Early depth test optimization
    if (lightDistance > farPlaneSq) {
        discard;
    }
    
    // Normalize and write depth
    lightDistance = sqrt(lightDistance) / pc.farPlane;
    gl_FragDepth = lightDistance;
}
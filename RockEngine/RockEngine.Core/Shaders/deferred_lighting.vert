#version 450 core
#include "Include/common.glsl"

layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {
   GlobalUBO ubo;
};

layout(location = 0) out vec3 cameraPosition;
layout(location = 1) out vec3 viewRay;

void main() {
    cameraPosition = ubo.camPos;
    vec2 positions[3] = vec2[](
        vec2(-1.0, -1.0),
        vec2(3.0, -1.0),
        vec2(-1.0, 3.0)
    );
    vec2 uv = positions[gl_VertexIndex] * 0.5 + 0.5;   // [0,1] with v=0 at top
    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);

    // Compute ray in view space by unprojecting an NDC point at near plane (z=0 for Vulkan)
    // Since we are using reversed-Z, depth near is 1.0, far 0.0. For the ray, we want the direction
    // from camera through the pixel on the near plane. Use NDC point with z=0 (since near maps to 1.0,
    // but for unprojecting a direction, z=0 or z=1? Actually, in reversed-Z, near plane is at depth=1.0.
    // We want a point on the near plane, so set depth = 1.0 in NDC, transform to view space,
    // and then the ray is that point (since camera at origin). Let's be precise:
    // NDC = (uv*2-1, 1.0, 1.0)  // x,y in [-1,1], z=1 (near plane in reversed-Z)
    // Transform by inverse(proj) to get view-space coordinate.
    vec4 clip = vec4(uv * 2.0 - 1.0, 1.0, 1.0);
    vec4 eye = ubo.invProj * clip;
    viewRay = eye.xyz / eye.w;    // this is the point on the near plane in view space
}
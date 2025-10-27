#version 460
#extension GL_KHR_vulkan_glsl : enable

layout(location = 0) in vec3 vWorldPos;

layout(location = 0) out vec4 oColor;

layout(push_constant) uniform PushConstants {
    vec3 cameraPosition;
    float gridScale;
    mat4 viewProj;
    mat4 model;
} push;

// Material properties
layout(set = 4, binding = 0) uniform GridMaterial {
    vec4 gridColor;
    vec4 majorGridColor;
    vec4 axisColor;
    vec4 axisColorZ;
    float gridStep;
    float majorGridStep;
} material;

float grid(vec2 worldPos, float scale) {
    vec2 coord = worldPos / scale;
    vec2 grid = abs(fract(coord - 0.5) - 0.5);
    float line = min(grid.x, grid.y);
    return 1.0 - smoothstep(0.0, 0.1 / scale, line);
}

float majorGrid(vec2 worldPos, float scale) {
    vec2 coord = worldPos / scale;
    vec2 grid = abs(fract(coord - 0.5) - 0.5);
    float line = min(grid.x, grid.y);
    return 1.0 - smoothstep(0.0, 0.15 / scale, line);
}

float axisLine(vec2 worldPos, float scale) {
    vec2 coord = worldPos / scale;
    vec2 distToAxis = abs(coord);
    float axis = min(distToAxis.x, distToAxis.y);
    return 1.0 - smoothstep(0.0, 0.02 / scale, axis);
}

void main() {
    vec2 worldXZ = vWorldPos.xz;
    
    // Calculate grid lines
    float gridLine = grid(worldXZ, material.gridStep * push.gridScale);
    float majorGridLine = majorGrid(worldXZ, material.majorGridStep * push.gridScale);
    float axisLineValue = axisLine(worldXZ, material.gridStep * push.gridScale);
    
    // Determine colors based on grid type
    vec4 color = material.gridColor;
    
    if (majorGridLine > 0.0) {
        color = mix(color, material.majorGridColor, majorGridLine);
    }
    
    if (axisLineValue > 0.0) {
        // Determine which axis we're on
        vec2 coord = worldXZ / (material.gridStep * push.gridScale);
        if (abs(coord.x) < abs(coord.y)) {
            color = mix(color, material.axisColor, axisLineValue);
        } else {
            color = mix(color, material.axisColorZ, axisLineValue);
        }
    } else if (gridLine > 0.0) {
        color = mix(vec4(0), color, gridLine);
    } else {
        discard;
    }
    
    // Distance-based fading
    vec2 cameraXZ = push.cameraPosition.xz;
    float distanceFromCamera = length(worldXZ - cameraXZ);
    float distanceFade = 1.0 - smoothstep(50.0, 200.0, distanceFromCamera);
    
    // Perspective fade (fade out at grazing angles)
    vec3 viewDir = normalize(vWorldPos - push.cameraPosition);
    float perspectiveFade = 1.0 - abs(dot(viewDir, vec3(0, 1, 0)));
    perspectiveFade = smoothstep(0.7, 1.0, perspectiveFade);
    
    float finalAlpha = color.a * distanceFade * (1.0 - perspectiveFade * 0.8);
    
    // Boost axis line visibility
    if (axisLineValue > 0.0) {
        finalAlpha = min(finalAlpha * 2.0, 1.0);
    }
    
    // Discard very transparent fragments
    if (finalAlpha < 0.05) {
        discard;
    }
    
    oColor = vec4(color.rgb, finalAlpha);
}
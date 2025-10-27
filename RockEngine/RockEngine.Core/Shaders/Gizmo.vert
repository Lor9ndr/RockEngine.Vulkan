#version 460
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec4 aColor;
layout(location = 3) in uint aAxisMask;

layout(set = 1, binding = 0) readonly buffer ModelData {
    mat4 models[];
};

layout(set = 0, binding = 0) uniform GlobalUbo_Dynamic {
    mat4 viewProj;
    vec3 camPos;
} ubo;

layout(push_constant) uniform PushConstants {
    vec4 gizmoColor;
    uint gizmoType;
} push;

layout(location = 0) out vec4 vColor;
layout(location = 1) out vec3 vNormal;
layout(location = 2) out vec3 vWorldPos;
layout(location = 3) out flat uint vAxisMask;

void main() {
    mat4 modelMatrix = models[gl_BaseInstance];
    
    // For rotate gizmo, maintain orientation but keep scale consistent
    if (push.gizmoType == 1) { // Rotate gizmo
        // Extract scale from model matrix
        vec3 scale = vec3(
            length(modelMatrix[0].xyz),
            length(modelMatrix[1].xyz),
            length(modelMatrix[2].xyz)
        );
        
        // Remove scale for proper ring rendering but maintain uniform scale
        float uniformScale = (scale.x + scale.y + scale.z) / 3.0;
        mat4 scaledModel = modelMatrix;
        scaledModel[0] = vec4(normalize(modelMatrix[0].xyz) * uniformScale, modelMatrix[0].w);
        scaledModel[1] = vec4(normalize(modelMatrix[1].xyz) * uniformScale, modelMatrix[1].w);
        scaledModel[2] = vec4(normalize(modelMatrix[2].xyz) * uniformScale, modelMatrix[2].w);
        
        vec4 worldPos = scaledModel * vec4(aPosition, 1.0);
        gl_Position = ubo.viewProj * worldPos;
        vWorldPos = worldPos.xyz;
        vNormal = mat3(scaledModel) * aNormal;
    } else {
        // Regular transform for translate and scale gizmos
        vec4 worldPos = modelMatrix * vec4(aPosition, 1.0);
        gl_Position = ubo.viewProj * worldPos;
        vWorldPos = worldPos.xyz;
        vNormal = mat3(modelMatrix) * aNormal;
    }
    
    vColor = aColor * push.gizmoColor;
    vAxisMask = aAxisMask; 
}
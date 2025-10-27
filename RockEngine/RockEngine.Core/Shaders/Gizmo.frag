#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) in vec4 vColor;
layout(location = 1) in vec3 vNormal;
layout(location = 2) in vec3 vWorldPos;
layout(location = 3) in flat uint vAxisMask;

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform FragmentPushConstants {
    vec4 gizmoColor;
    uint gizmoType;
} push;

void main() {
    // Enhanced lighting for better 3D appearance
    vec3 lightDir = normalize(vec3(0.5, 1.0, 0.5));
    vec3 normal = normalize(vNormal);
    
    float diff = max(dot(normal, lightDir), 0.3);
    float ambient = 0.4;
    
    // Rim lighting for better visibility
    vec3 viewDir = normalize(-vWorldPos);
    float rim = 1.0 - max(dot(normal, viewDir), 0.0);
    rim = smoothstep(0.4, 1.0, rim) * 0.3;
    
    // Apply hover effect only to the specific axis
    vec3 finalColor = vColor.rgb * (ambient + diff) + rim;
    
    // Add some specular highlight for metallic look
    vec3 reflectDir = reflect(-lightDir, normal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 16.0);
    finalColor += spec * 0.2;
    
    outColor = vec4(finalColor, vColor.a);
}
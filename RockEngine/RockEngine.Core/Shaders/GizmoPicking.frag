#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) out vec4 outColor;

layout(location = 3) in flat uint vAxisMask;

layout(push_constant) uniform FragmentPushConstants {
    vec4 gizmoColor;
    uint gizmoType;
} push;

vec4 gizmoHandleToColor(uint gizmoType, uint axisMask) {
    // Reserve high values for gizmos (e.g., 0xFF000000 range)
    // Format: [gizmoType:8 bits][axisMask:8 bits][reserved:16 bits]
    uint encoded = 0;
    
    // Set high byte to indicate this is a gizmo (not a regular entity)
    encoded |= (0xFFu << 24);
    
    // Store gizmo type in next byte
    encoded |= ((gizmoType & 0xFFu) << 16);
    
    // Store axis mask in next byte
    encoded |= ((axisMask & 0xFFu) << 8);
    
    // Convert to color
    float r = float((encoded >> 0) & 0xFFu) / 255.0;
    float g = float((encoded >> 8) & 0xFFu) / 255.0;
    float b = float((encoded >> 16) & 0xFFu) / 255.0;
    float a = float((encoded >> 24) & 0xFFu) / 255.0;
    
    return vec4(r, g, b, a);
}

void main() {
    outColor = gizmoHandleToColor(push.gizmoType, vAxisMask);
}
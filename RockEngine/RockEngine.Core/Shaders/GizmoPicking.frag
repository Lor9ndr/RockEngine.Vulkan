#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) out vec4 outColor;

layout(location = 3) in flat uint vAxisMask;

layout(push_constant) uniform FragmentPushConstants {
    vec4 gizmoColor;
    uint gizmoType;
} push;

// Robust encoding: [1 bit: isGizmo] [7 bits: gizmoType] [8 bits: axisMask] [16 bits: checksum]
uvec4 encodeGizmoHandle(uint gizmoType, uint axisMask) {
    // Calculate a simple checksum to detect corruption
    uint checksum = (gizmoType ^ axisMask) & 0xFFFF;
    
    // Pack the data
    uint packed = 0;
    packed |= (1u << 31);                          // Bit 31: Always 1 for gizmo
    packed |= ((gizmoType & 0x7Fu) << 24);         // Bits 24-30: Gizmo type (7 bits)
    packed |= ((axisMask & 0xFFu) << 16);          // Bits 16-23: Axis mask (8 bits)  
    packed |= (checksum & 0xFFFFu);                // Bits 0-15: Checksum (16 bits)
    
    // Split into bytes (little-endian: R=LSB, A=MSB)
    return uvec4(
        (packed >> 0) & 0xFFu,   // R: bits 0-7
        (packed >> 8) & 0xFFu,   // G: bits 8-15  
        (packed >> 16) & 0xFFu,  // B: bits 16-23
        (packed >> 24) & 0xFFu   // A: bits 24-31
    );
}

vec4 gizmoHandleToColor(uint gizmoType, uint axisMask) {
    uvec4 encoded = encodeGizmoHandle(gizmoType, axisMask);
    return vec4(encoded) / 255.0;
}

void main() {
    outColor = gizmoHandleToColor(push.gizmoType, vAxisMask);
}
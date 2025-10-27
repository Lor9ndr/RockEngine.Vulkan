#version 450
#extension GL_KHR_vulkan_glsl:enable

layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    uint entityId;
} pushConstants;

vec4 entityIdToColor(uint entityId) {
    // Convert entity ID to RGBA color (0-255 range)
    float r = float((entityId >> 0) & 0xFF) / 255.0;
    float g = float((entityId >> 8) & 0xFF) / 255.0;
    float b = float((entityId >> 16) & 0xFF) / 255.0;
    float a = float((entityId >> 24) & 0xFF) / 255.0;
    return vec4(r, g, b, a);
}

void main() {
    outColor = entityIdToColor(pushConstants.entityId);
}
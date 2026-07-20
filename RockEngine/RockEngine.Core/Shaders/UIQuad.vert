#version 450
#extension GL_EXT_nonuniform_qualifier : require

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;
layout(location = 3) in uint aTextureId;

layout(push_constant) uniform PushConstants {
    mat4 projection;
} pc;

layout(location = 0) out vec2 vTexCoord;
layout(location = 1) out vec4 vColor;
layout(location = 2) flat out uint vTextureId;

void main() {
    gl_Position = pc.projection * vec4(aPosition, 0.0, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
    vTextureId = aTextureId;
}
#version 450
#extension GL_EXT_nonuniform_qualifier : require

layout(set = 0, binding = 0) uniform sampler2D uTextures[];

layout(location = 0) in vec2 vTexCoord;
layout(location = 1) in vec4 vColor;
layout(location = 2) flat in uint vTextureId;

layout(location = 0) out vec4 outColor;

void main() {
    outColor = texture(uTextures[vTextureId], vTexCoord) * vColor;
}
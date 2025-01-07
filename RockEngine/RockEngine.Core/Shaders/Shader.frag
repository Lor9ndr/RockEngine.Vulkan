#version 450

layout(location = 0) in vec4 fragPos;
layout(location = 1) in vec2 texCoords;
layout(location = 2) in vec3 normals;
layout(location = 0) out vec4 outColor;



void main() {

    outColor = vec4(normals, 1);
}

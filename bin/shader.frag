#version 330 core

in vec3 vColor;
in vec2 vTexCoord;

out vec4 FragColor;

uniform sampler2D texture0;

void main()
{
    vec4 texColor = texture(texture0, vTexCoord);
    FragColor = vec4(vColor, 1.0) * texColor;
}

﻿using Helion.Render.OpenGL.Buffer.Vao;
using Helion.Render.OpenGL.Shader;
using Helion.Util;

namespace Helion.Render.OpenGL.Renderers.World
{
    public static class WorldShader
    {
        public static ShaderProgram CreateShaderProgramOrThrow(VertexArrayObject? vao = null)
        {
            ShaderBuilder builder = new ShaderBuilder();

            builder.VertexShaderText = @"
                #version 130
                
                in vec3 pos;
                in vec2 uv;
                in float alpha;
                in float unitBrightness;

                out vec2 uvFrag;
                out float alphaFrag;
                out float unitBrightnessFrag;

                uniform mat4 mvp;

                void main() {
                    uvFrag = uv;
                    alphaFrag = alpha;
                    unitBrightnessFrag = unitBrightness;
                    gl_Position = mvp * vec4(pos, 1.0);
                }
            ";

            builder.FragmentShaderText = @"
                #version 130

                in vec2 uvFrag;
                in float alphaFrag;
                in float unitBrightnessFrag;

                out vec4 fragColor;

                uniform sampler2D boundTexture;

                void main() {
                    fragColor = texture(boundTexture, uvFrag);
                    fragColor.xyz = fragColor.xyz * unitBrightnessFrag;
                    fragColor.w *= alphaFrag;

                    if (fragColor.w <= 0.0)
                        discard;
                }
            ";

            ShaderProgram? program = ShaderProgram.Create(builder, vao);
            if (program == null)
                throw new HelionException("Unexpected failure when creating world shader");
            return program;
        }
    }
}

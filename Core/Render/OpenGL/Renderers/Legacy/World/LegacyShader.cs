using Helion.Render.OpenGL.Context;
using Helion.Render.OpenGL.Shader;
using Helion.Render.OpenGL.Shader.Component;
using Helion.Render.OpenGL.Shader.Fields;
using Helion.Render.OpenGL.Vertex;

namespace Helion.Render.OpenGL.Renderers.Legacy.World
{
    public class LegacyShader : ShaderProgram
    {
        public readonly UniformInt HasInvulnerability = new();
        public readonly UniformInt BoundTexture = new();
        public readonly UniformFloat LightLevelMix = new();
        public readonly UniformFloat LightLevelValue = new();
        public readonly UniformMatrix4 Mvp = new();

        public LegacyShader(IGLFunctions functions, ShaderBuilder builder, VertexArrayAttributes attributes) :
            base(functions, builder, attributes)
        {
        }

        public static ShaderBuilder MakeBuilder(IGLFunctions functions)
        {
            const string vertexShaderText = @"
                #version 130

                in vec3 pos;
                in vec2 uv;
                in float lightLevel;
                in float alpha;
                in vec3 colorMul;

                out vec2 uvFrag;
                flat out float lightLevelFrag;
                flat out float alphaFrag;
                out vec3 colorMulFrag;

                uniform mat4 mvp;

                void main() {
                    uvFrag = uv;    
                    lightLevelFrag = clamp(lightLevel, 0.0, 1.0);
                    alphaFrag = alpha;
                    colorMulFrag = colorMul;

                    gl_Position = mvp * vec4(pos, 1.0);
                }
            ";

            const string fragmentShaderText = @"
                #version 130

                in vec2 uvFrag;
                flat in float lightLevelFrag;
                flat in float alphaFrag;
                in vec3 colorMulFrag;

                out vec4 fragColor;

                uniform int hasInvulnerability;
                uniform float lightLevelMix;
                uniform float lightLevelValue;
                uniform sampler2D boundTexture;

                float calculateLightLevel() {
                    float lightLevel = lightLevelFrag;

                    if (lightLevel <= 0.75) {
	                    if (lightLevel > 0.4) {
		                    lightLevel = -0.6375 + (1.85 * lightLevel);
		                    if (lightLevel < 0.08) {
			                    lightLevel = 0.08 + (lightLevel * 0.2);
		                    }
	                    } else {
		                    lightLevel /= 5.0;
	                    }
                    }
  
                    return mix(clamp(lightLevel, 0.0, 1.0), lightLevelValue, lightLevelMix);
                }

                void main() {
                    fragColor = texture(boundTexture, uvFrag.st);
                    fragColor.xyz *= colorMulFrag;
                    fragColor.xyz *= calculateLightLevel();
                    fragColor.w *= alphaFrag;

                    if (fragColor.w <= 0.0)
                        discard;

                    // If invulnerable, grayscale everything and crank the brightness.
                    // Note: The 1.5x is a visual guess to make it look closer to vanilla.
                    if (hasInvulnerability != 0)
                    {
                        float maxColor = max(max(fragColor.x, fragColor.y), fragColor.z);
                        maxColor *= 1.5;
                        fragColor.xyz = vec3(maxColor, maxColor, maxColor);
                    }
                }
            ";

            VertexShaderComponent vertexShaderComponent = new(functions, vertexShaderText);
            FragmentShaderComponent fragmentShaderComponent = new(functions, fragmentShaderText);
            return new ShaderBuilder(vertexShaderComponent, fragmentShaderComponent);
        }
    }
}
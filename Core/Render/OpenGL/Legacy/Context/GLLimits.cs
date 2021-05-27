using Helion.Render.OpenGL.Legacy.Context.Types;

namespace Helion.Render.OpenGL.Legacy.Context
{
    public class GLLimits
    {
        public readonly float MaxAnisotropy;
        
        public GLLimits(IGLFunctions gl)
        {
            MaxAnisotropy = gl.GetFloat(GetFloatType.MaxTextureMaxAnisotropyExt);
            // TODO: GL_MAX_UNIFORM_BUFFER_BINDINGS
            // TODO: GL_MAX_SHADER_STORAGE_BUFFER_BINDINGS
        }
    }
}
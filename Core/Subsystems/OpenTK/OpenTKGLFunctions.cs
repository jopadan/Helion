using System;
using System.Runtime.InteropServices;
using Helion.Render.OpenGL.Context;
using Helion.Render.OpenGL.Context.Types;
using OpenTK.Graphics.OpenGL;

namespace Helion.Subsystems.OpenTK
{
    public class OpenTKGLFunctions : IGLFunctions
    {
        public void AttachShader(int programId, int shaderId)
        {
            GL.AttachShader(programId, shaderId);
        }

        public void BindAttribLocation(int programId, int attrIndex, string attrName)
        {
            GL.BindAttribLocation(programId, attrIndex, attrName);
        }

        public void BindBuffer(BufferType type, int bufferId)
        {
            GL.BindBuffer((BufferTarget)type, bufferId);
        }

        public void BindVertexArray(int vaoId)
        {
            GL.BindVertexArray(vaoId);
        }

        public void BlendFunc(BlendingFactorType sourceFactor, BlendingFactorType destFactor)
        {
            GL.BlendFunc((BlendingFactor)sourceFactor, (BlendingFactor)destFactor);
        }

        public void BufferData<T>(BufferType bufferType, int totalBytes, T[] data, BufferUsageType usageType) where T : struct
        {
            GL.BufferData((BufferTarget)bufferType, totalBytes, data, (BufferUsageHint)usageType);
        }
        
        public void Clear(ClearType type)
        {
            GL.Clear((ClearBufferMask)type);
        }
        
        public void ClearColor(float r, float g, float b, float a)
        {
            GL.ClearColor(r, g, b, a);
        }

        public void CompileShader(int shaderId)
        {
            GL.CompileShader(shaderId);
        }

        public int CreateProgram()
        {
            return GL.CreateProgram();
        }

        public int CreateShader(ShaderComponentType type)
        {
            return GL.CreateShader((ShaderType)type);
        }

        public void CullFace(CullFaceType type)
        {
            GL.CullFace((CullFaceMode)type);
        }

        public void DebugMessageCallback(Action<DebugLevel, string> callback)
        {
            GL.DebugMessageCallback((source, type, id, severity, length, message, userParam) =>
            {
                string msg = Marshal.PtrToStringAnsi(message, length);
                callback((DebugLevel)severity, msg);
            }, IntPtr.Zero);
        }

        public void DeleteBuffer(int bufferId)
        {
            GL.DeleteBuffer(bufferId);
        }

        public void DeleteProgram(int programId)
        {
            GL.DeleteProgram(programId);
        }

        public void DeleteShader(int shaderId)
        {
            GL.DeleteShader(shaderId);
        }

        public void DeleteTexture(int textureId)
        {
            GL.DeleteTexture(textureId);
        }

        public void DeleteVertexArray(int vaoId)
        {
            GL.DeleteVertexArray(vaoId);
        }

        public void DetachShader(int programId, int shaderId)
        {
            GL.DetachShader(programId, shaderId);
        }

        public void DrawArrays(PrimitiveDrawType type, int startIndex, int count)
        {
            GL.DrawArrays((PrimitiveType)type, startIndex, count);
        }

        public void Enable(EnableType type)
        {
            GL.Enable((EnableCap)type);
        }

        public void EnableVertexAttribArray(int index)
        {
            GL.EnableVertexAttribArray(index);
        }

        public void FrontFace(FrontFaceType type)
        {
            GL.FrontFace((FrontFaceDirection)type);
        }

        public int GenBuffer()
        {
            return GL.GenBuffer();
        }

        public int GenVertexArray()
        {
            return GL.GenVertexArray();
        }

        public string GetActiveUniform(int programId, int uniformIndex, out int size, out int typeEnum)
        {
            string result = GL.GetActiveUniform(programId, uniformIndex, out size, out ActiveUniformType type);
            typeEnum = (int)type;
            return result;
        }

        public ErrorType GetError()
        {
            ErrorCode errorCode = GL.GetError();
            return errorCode == ErrorCode.NoError ? ErrorType.None : (ErrorType)errorCode;
        }
        
        public int GetInteger(GetIntegerType type)
        {
            return GL.GetInteger((GetPName)type);
        }

        public void GetProgram(int programId, GetProgramParameterType type, out int value)
        {
            GL.GetProgram(programId, (GetProgramParameterName)type, out value);
        }

        public string GetProgramInfoLog(int programId)
        {
            return GL.GetProgramInfoLog(programId);
        }

        public void GetShader(int shaderId, ShaderParameterType type, out int value)
        {
            GL.GetShader(shaderId, (ShaderParameter)type, out value);
        }

        public string GetShaderInfoLog(int shaderId)
        {
            return GL.GetShaderInfoLog(shaderId);
        }

        public string GetString(GetStringType type)
        {
            return GL.GetString((StringName)type);
        }

        public string GetString(GetStringType type, int index)
        {
            return GL.GetString((StringNameIndexed)type, index);
        }

        public int GetUniformLocation(int programId, string name)
        {
            return GL.GetUniformLocation(programId, name);
        }

        public void LinkProgram(int programId)
        {
            GL.LinkProgram(programId);
        }

        public void ObjectLabel(ObjectLabelType type, int objectId, string name)
        {
            GL.ObjectLabel((ObjectLabelIdentifier)type, objectId, name.Length, name);
        }

        public void PolygonMode(PolygonFaceType faceType, PolygonModeType fillType)
        {
            GL.PolygonMode((MaterialFace)faceType, (PolygonMode)fillType);
        }

        public void ShaderSource(int shaderId, string sourceText)
        {
            GL.ShaderSource(shaderId, sourceText);
        }

        public void Uniform1(int location, int value)
        {
            GL.Uniform1(location, value);
        }

        public void Uniform1(int location, float value)
        {
            GL.Uniform1(location, value);
        }

        public void UseProgram(int programId)
        {
            GL.UseProgram(programId);
        }

        public void VertexAttribIPointer(int index, int size, VertexAttributeIntegralPointerType type, int stride, int offset)
        {
            GL.VertexAttribIPointer(index, size, (VertexAttribIntegerType)type, stride, new IntPtr(offset));
        }

        public void VertexAttribPointer(int index, int byteLength, VertexAttributePointerType type, bool normalized, int stride, int offset)
        {
            GL.VertexAttribPointer(index, byteLength, (VertexAttribPointerType)type, normalized, stride, new IntPtr(offset));
        }

        public void Viewport(int x, int y, int width, int height)
        {
            GL.Viewport(x, y, width, height);
        }
    }
}
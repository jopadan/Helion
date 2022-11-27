using System;
using System.Collections.Generic;
using Helion.Render.OpenGL.Context;
using Helion.Render.OpenGL.Shader;
using Helion.Render.OpenGL.Texture.Legacy;
using Helion.Util;
using static Helion.Util.Assertion.Assert;

namespace Helion.Render.OpenGL.Renderers.Legacy.World.Data;

public class RenderWorldDataManager : IDisposable
{
    private readonly DataCache m_dataCache;
    private readonly List<RenderWorldData> m_renderData = new();
    private readonly List<RenderWorldData> m_alphaRenderData = new();
    private RenderWorldData?[] m_allRenderData;

    public RenderWorldDataManager(DataCache dataCache)
    {
        m_allRenderData = new RenderWorldData[1024];
        m_dataCache = dataCache;
    }

    ~RenderWorldDataManager()
    {
        ReleaseUnmanagedResources();
    }

    public RenderWorldData GetRenderData(GLLegacyTexture texture, RenderProgram program)
    {
        if (m_allRenderData.Length <= texture.TextureId)
        {
            var original = m_allRenderData;
            m_allRenderData = new RenderWorldData[texture.TextureId + 1024];
            Array.Copy(original, m_allRenderData, original.Length);
        }

        RenderWorldData? data = m_allRenderData[texture.TextureId];
        if (data != null)
            return data;

        RenderWorldData newData = new(texture, program);
        m_allRenderData[texture.TextureId] = newData;       
        m_renderData.Add(newData);
        return newData;
    }

    public RenderWorldData GetAlphaRenderData(GLLegacyTexture texture, RenderProgram program)
    {
        // Since we have to order transparency drawing we can't store all the vbo data in the same texture
        // This will be a large performance penalty because we potentially have to switch textures often in the renderer
        // The RenderWorldData is at least cached new ones are not created on every render loop
        RenderWorldData data = m_dataCache.GetAlphaRenderWorldData(texture, program);
        m_alphaRenderData.Add(data);
        return data;
    }

    public void Clear()
    {
        for (int i = 0; i < m_allRenderData.Length; i++)
        {
            var data = m_allRenderData[i];
            if (data != null)
                data.Clear();
        }
        for (int i = 0; i < m_renderData.Count; i++)
            m_renderData[i].Clear();
        for (int i = 0; i < m_alphaRenderData.Count; i++)
            m_dataCache.FreeAlphaRenderWorldData(m_alphaRenderData[i]);
        m_alphaRenderData.Clear();
    }

    public void DrawNonAlpha()
    {
        for (int i = 0; i < m_renderData.Count; i++)
            m_renderData[i].Draw();
    }

    public void DrawAlpha()
    {
        for (int i = 0; i < m_alphaRenderData.Count; i++)
            m_alphaRenderData[i].Draw();
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    private void ReleaseUnmanagedResources()
    {
        for (int i = 0; i < m_renderData.Count; i++)
            m_renderData[i].Dispose();
    }
}

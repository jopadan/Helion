using System;
using System.Drawing;
using Helion.Geometry;
using Helion.Geometry.Vectors;
using Helion.Graphics.String;
using Helion.Render.Legacy.Shared;
using Helion.Resources;

namespace Helion.Render.Legacy.Texture.Legacy
{
    public class GLLegacyImageDrawInfoProvider : IImageDrawInfoProvider
    {
        private readonly LegacyGLTextureManager m_textureManager;

        public GLLegacyImageDrawInfoProvider(LegacyGLTextureManager textureManager)
        {
            m_textureManager = textureManager;
        }

        public bool ImageExists(string image)
        {
            return m_textureManager.Contains(image);
        }

        public Dimension GetImageDimension(string image, ResourceNamespace resourceNamespace = ResourceNamespace.Global)
        {
            m_textureManager.TryGet(image, resourceNamespace, out GLLegacyTexture texture);
            return texture.Dimension;
        }

        public Vec2I GetImageOffset(string image, ResourceNamespace resourceNamespace = ResourceNamespace.Global)
        {
            m_textureManager.TryGet(image, resourceNamespace, out GLLegacyTexture texture);
            return texture.Metadata.Offset;
        }

        public int GetFontHeight(string font) => m_textureManager.GetFont(font).Height;

        public Dimension GetDrawArea(ColoredString str, string font, int fontSize, int maxWidth, bool wrap)
        {
            GLFontTexture<GLLegacyTexture> fontTexture = m_textureManager.GetFont(font);

            float scaleFactor = (float)fontSize / GetFontHeight(font);
            int finalWidth = 0;
            int currentWidth = 0;
            bool notFirstChar = false;
            int rowsOfCharacters = 1;

            foreach (ColoredChar c in str)
            {
                int charWidth = (int)(fontTexture[c.Character].Image.Width * scaleFactor);

                if (notFirstChar && currentWidth + charWidth >= maxWidth)
                {
                    finalWidth = Math.Max(finalWidth, currentWidth);
                    currentWidth = 0;
                    rowsOfCharacters++;
                }

                currentWidth += charWidth;
                notFirstChar = true;
            }

            finalWidth = Math.Max(finalWidth, currentWidth);
            int finalHeight = (int)(rowsOfCharacters * fontTexture.Height * scaleFactor);

            return new Dimension(finalWidth, finalHeight);
        }

        public Rectangle GetDrawArea(ColoredString str, string font, Vec2I topLeft)
        {
            GLFontTexture<GLLegacyTexture> fontTexture = m_textureManager.GetFont(font);

            int width = 0;
            foreach (var c in str)
                width += fontTexture[c.Character].Image.Width;
            return new Rectangle(topLeft.X, topLeft.Y, width, fontTexture.Height);
        }
    }
}
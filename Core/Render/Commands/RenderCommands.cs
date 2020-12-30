using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Helion.Graphics.Fonts.Renderable;
using Helion.Graphics.Geometry;
using Helion.Render.Commands.Types;
using Helion.Render.Shared;
using Helion.Util;
using Helion.Util.Configuration;
using Helion.Util.Geometry;
using Helion.Util.Geometry.Vectors;
using Helion.Util.Time;
using Helion.World;
using Helion.World.Entities;

namespace Helion.Render.Commands
{
    public class RenderCommands : IEnumerable<IRenderCommand>
    {
        public readonly Config Config;
        public readonly Dimension WindowDimension;
        public readonly IImageDrawInfoProvider ImageDrawInfoProvider;
        public readonly FpsTracker FpsTracker;
        public ResolutionInfo ResolutionInfo { get; private set; }
        private readonly List<IRenderCommand> m_commands = new();
        private Vec2D m_scale = Vec2D.One;
        private int m_centeringOffsetX;

        public RenderCommands(Config config, Dimension windowDimensions, IImageDrawInfoProvider imageDrawInfoProvider,
            FpsTracker fpsTracker)
        {
            Config = config;
            WindowDimension = windowDimensions;
            ResolutionInfo = new ResolutionInfo { VirtualDimensions = windowDimensions };
            ImageDrawInfoProvider = imageDrawInfoProvider;
            FpsTracker = fpsTracker;
        }

        public void Clear()
        {
            m_commands.Add(ClearRenderCommand.All());
        }

        public void ClearDepth()
        {
            m_commands.Add(ClearRenderCommand.DepthOnly());
        }

        public void DrawImage(CIString textureName, int left, int top, int width, int height, Color color,
            float alpha = 1.0f)
        {
            (int x, int y, int w, int h) = TranslateDimensions(left, top, width, height);
            ImageBox2I drawArea = new(x, y, x + w, y + h);
            DrawImageCommand cmd = new(textureName, drawArea, color, alpha);
            m_commands.Add(cmd);
        }

        public void FillRect(ImageBox2I rectangle, Color color, float alpha)
        {
            ImageBox2I transformedRectangle = TranslateDimensions(rectangle);
            DrawShapeCommand command = new(transformedRectangle, color, alpha);
            m_commands.Add(command);
        }

        public void DrawText(RenderableString str, int left, int top, float alpha)
        {
            ImageBox2I drawArea = TranslateDimensions(left, top, str.DrawArea);
            DrawTextCommand command = new(str, drawArea, alpha);
            m_commands.Add(command);
        }

        public void DrawWorld(WorldBase world, Camera camera, int gametick, float fraction, Entity viewerEntity)
        {
            m_commands.Add(new DrawWorldCommand(world, camera, gametick, fraction, viewerEntity));
        }

        public void Viewport(Dimension dimension, Vec2I? offset = null)
        {
            m_commands.Add(new ViewportCommand(dimension, offset ?? Vec2I.Zero));
        }

        /// <summary>
        /// Sets a virtual resolution to draw with.
        /// </summary>
        /// <param name="width">The virtual window width.</param>
        /// <param name="height">The virtual window height.</param>
        /// <param name="scale">How to scale drawing.</param>
        public void SetVirtualResolution(int width, int height, ResolutionScale scale = ResolutionScale.None)
        {
            Dimension dimension = new Dimension(width, height);
            ResolutionInfo info = new() { VirtualDimensions = dimension, Scale = scale };
            SetVirtualResolution(info);
        }

        /// <summary>
        /// Sets a virtual resolution to draw with.
        /// </summary>
        /// <param name="resolutionInfo">Resolution parameters.</param>
        public void SetVirtualResolution(ResolutionInfo resolutionInfo)
        {
            ResolutionInfo = resolutionInfo;
            Dimension virtualDimension = resolutionInfo.VirtualDimensions;

            Vec2I windowDim = WindowDimension.ToVector();
            Vec2I virtualDim = virtualDimension.ToVector();
            m_scale = windowDim.ToDouble() / virtualDim.ToDouble();
            m_centeringOffsetX = 0;

            // By default we're stretching, but if we're centering, our values
            // have to change to accomodate a gutter if the aspect ratios are
            // different.
            if (resolutionInfo.Scale == ResolutionScale.Center)
            {
                // We only want to do centering if we will end up with gutters
                // on the side. This can only happen if the virtual dimension
                // has a smaller aspect ratio. We have to exit out if not since
                // it will cause weird overdrawing otherwise.
                if (WindowDimension.AspectRatio > virtualDimension.AspectRatio)
                {
                    m_scale.X = m_scale.Y;
                    m_centeringOffsetX = (WindowDimension.Width - (int)(virtualDimension.Width * m_scale.X)) / 2;
                }
            }
        }

        /// <summary>
        /// Restores drawing to the native resolution (viewport size, no scale
        /// transformations).
        /// </summary>
        public void UseNativeResolution()
        {
            ResolutionInfo = new ResolutionInfo
            {
                VirtualDimensions = WindowDimension,
                Scale = ResolutionScale.None
            };
        }

        public IEnumerator<IRenderCommand> GetEnumerator() => m_commands.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private ImageBox2I TranslateDimensions(int x, int y, Dimension dimension)
        {
            return TranslateDimensions(new ImageBox2I(x, y, x + dimension.Width, y + dimension.Height));
        }

        private (int x, int y, int w, int h) TranslateDimensions(int x, int y, int width, int height)
        {
            ImageBox2I drawArea = TranslateDimensions(new ImageBox2I(x, y, x + width, y + height));
            return (drawArea.Left, drawArea.Top, drawArea.Right, drawArea.Bottom);
        }

        private ImageBox2I TranslateDimensions(ImageBox2I drawArea)
        {
            if (WindowDimension == ResolutionInfo.VirtualDimensions)
                return drawArea;

            Vec2I start = TranslatePoint(drawArea.Left, drawArea.Top);
            Vec2I end = TranslatePoint(drawArea.Right, drawArea.Bottom);
            return new ImageBox2I(start.X + m_centeringOffsetX, start.Y, end.X, end.Y);
        }

        private Vec2I TranslatePoint(int x, int y) => (new Vec2D(x, y) * m_scale).ToInt();
    }
}
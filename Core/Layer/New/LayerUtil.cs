﻿using System.Collections.Generic;
using System.Linq;
using Helion.Render.Common.Renderers;

namespace Helion.Layer.New
{
    public static class LayerUtil
    {
        public static IList<string> GetRenderPages(IHudRenderContext hud, IList<string> pages,
            bool repeatIfNotExists)
        {
            if (pages.Count == 0)
                return pages;

            string lastPage = pages[0];
            List<string> newPages = new() { lastPage };

            foreach (string page in pages.Skip(1))
            {
                if (!hud.ImageExists(page))
                {
                    if (repeatIfNotExists)
                        newPages.Add(lastPage);
                }
                else
                {
                    lastPage = page;
                    newPages.Add(page);
                }
            }

            return newPages;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Helion.Geometry;
using Helion.Geometry.Vectors;
using Helion.Render.OpenGL.Context;
using Helion.Render.OpenGL.Renderers.Legacy.World.Data;
using Helion.Render.OpenGL.Renderers.Legacy.World.Geometry.Portals;
using Helion.Render.OpenGL.Renderers.Legacy.World.Geometry.Static;
using Helion.Render.OpenGL.Renderers.Legacy.World.Sky;
using Helion.Render.OpenGL.Renderers.Legacy.World.Sky.Sphere;
using Helion.Render.OpenGL.Shader;
using Helion.Render.OpenGL.Shared;
using Helion.Render.OpenGL.Shared.World;
using Helion.Render.OpenGL.Texture.Legacy;
using Helion.Render.OpenGL.Textures;
using Helion.Resources;
using Helion.Resources.Archives.Collection;
using Helion.Util;
using Helion.Util.Configs;
using Helion.Util.Container;
using Helion.World;
using Helion.World.Geometry.Lines;
using Helion.World.Geometry.Sectors;
using Helion.World.Geometry.Sides;
using Helion.World.Geometry.Subsectors;
using Helion.World.Geometry.Walls;
using Helion.World.Physics;
using Helion.World.Static;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static Helion.World.Geometry.Sectors.Sector;

namespace Helion.Render.OpenGL.Renderers.Legacy.World.Geometry;

public class GeometryRenderer : IDisposable
{
    private const double MaxSky = 16384;
    private static readonly Sector DefaultSector = CreateDefault();

    public readonly List<IRenderObject> AlphaSides = new();
    public readonly PortalRenderer Portals;
    private readonly IConfig m_config;
    private readonly RenderProgram m_program;
    private readonly LegacyGLTextureManager m_glTextureManager;
    private readonly LineDrawnTracker m_lineDrawnTracker = new();
    private readonly StaticCacheGeometryRenderer m_staticCacheGeometryRenderer;
    private readonly DynamicArray<TriangulatedWorldVertex> m_subsectorVertices = new();
    private readonly LegacyVertex[] m_wallVertices = new LegacyVertex[6];
    private readonly SkyGeometryVertex[] m_skyWallVertices = new SkyGeometryVertex[6];
    private readonly RenderWorldDataManager m_worldDataManager;
    private readonly LegacySkyRenderer m_skyRenderer;
    private readonly ArchiveCollection m_archiveCollection;
    private readonly MidTextureHack m_midTextureHack = new();
    private GLBufferTexture? m_lightBuffer;
    private double m_tickFraction;
    private bool m_skyOverride;
    private bool m_floorChanged;
    private bool m_ceilingChanged;
    private bool m_sectorChangedLine;
    private bool m_cacheOverride;
    private bool m_vanillaFlood;
    private bool m_alwaysFlood;
    private bool m_fakeContrast;
    private Vec3D m_viewPosition;
    private Vec3D m_prevViewPosition;
    private Sector m_viewSector;
    private IWorld m_world;
    private TransferHeightView m_transferHeightsView = TransferHeightView.Middle;
    private bool m_buffer = true;
    private LegacyVertex[]?[] m_vertexLookup = Array.Empty<LegacyVertex[]>();
    private LegacyVertex[]?[] m_vertexLowerLookup = Array.Empty<LegacyVertex[]>();
    private LegacyVertex[]?[] m_vertexUpperLookup = Array.Empty<LegacyVertex[]>();
    private SkyGeometryVertex[]?[] m_skyWallVertexLowerLookup = Array.Empty<SkyGeometryVertex[]>();
    private SkyGeometryVertex[]?[] m_skyWallVertexUpperLookup = Array.Empty<SkyGeometryVertex[]>();
    private DynamicArray<LegacyVertex[][]?> m_vertexFloorLookup = new(3);
    private DynamicArray<LegacyVertex[][]?> m_vertexCeilingLookup = new(3);
    private DynamicArray<SkyGeometryVertex[][]?> m_skyFloorVertexLookup = new(3);
    private DynamicArray<SkyGeometryVertex[][]?> m_skyCeilingVertexLookup = new(3);
    // List of each subsector mapped to a sector id
    private DynamicArray<Subsector>[] m_subsectors = Array.Empty<DynamicArray<Subsector>>();
    private int[] m_drawnSides = Array.Empty<int>();
    private float[] m_lightBufferData = Array.Empty<float>();

    private TextureManager TextureManager => m_archiveCollection.TextureManager;

    public GeometryRenderer(IConfig config, ArchiveCollection archiveCollection, LegacyGLTextureManager glTextureManager,
        RenderProgram program, RenderProgram staticProgram, RenderWorldDataManager worldDataManager)
    {
        m_config = config;
        m_program = program;
        m_glTextureManager = glTextureManager;
        m_worldDataManager = worldDataManager;
        Portals = new(archiveCollection, glTextureManager);
        m_skyRenderer = new LegacySkyRenderer(archiveCollection, glTextureManager);
        m_viewSector = DefaultSector;
        m_archiveCollection = archiveCollection;
        m_staticCacheGeometryRenderer = new(archiveCollection, glTextureManager, staticProgram, this);

        for (int i = 0; i < m_wallVertices.Length; i++)
            m_wallVertices[i].Alpha = 1.0f;

        m_world = null!;
    }

    ~GeometryRenderer()
    {
        ReleaseUnmanagedResources();
    }

    public void UpdateTo(IWorld world)
    {
        m_world = world;
        if (!world.SameAsPreviousMap)
            m_skyRenderer.Reset();
        m_lineDrawnTracker.UpdateToWorld(world);
        m_viewSector = DefaultSector;

        m_vanillaFlood = world.Config.Render.VanillaFloodFill.Value;
        m_alwaysFlood = world.Config.Render.AlwaysFloodFillFlats.Value;

        PreloadAllTextures(world);

        int sideCount = world.Sides.Count;
        int sectorCount = world.Sectors.Count;
        bool freeData = !world.SameAsPreviousMap;
        m_vertexLookup = UpdateVertexWallLookup(m_vertexLookup, sideCount, freeData);
        m_vertexLowerLookup = UpdateVertexWallLookup(m_vertexLowerLookup, sideCount, freeData);
        m_vertexUpperLookup = UpdateVertexWallLookup(m_vertexUpperLookup, sideCount, freeData);
        m_skyWallVertexLowerLookup = UpdateSkyWallLookup(m_skyWallVertexLowerLookup, sideCount, freeData);
        m_skyWallVertexUpperLookup = UpdateSkyWallLookup(m_skyWallVertexUpperLookup, sideCount, freeData);
        UpdateFlatVertices(m_vertexFloorLookup, sectorCount, freeData);
        UpdateFlatVertices(m_vertexCeilingLookup, sectorCount, freeData);
        UpdateSkyFlatVertices(m_skyFloorVertexLookup, sectorCount, freeData);
        UpdateSkyFlatVertices(m_skyCeilingVertexLookup, sectorCount, freeData);

        if (!world.SameAsPreviousMap)
        {
            for (int i = 0; i < m_subsectors.Length; i++)
            {
                m_subsectors[i].FlushReferences();
                m_subsectors[i].Clear();
            }

            if (m_subsectors.Length < world.Sectors.Count)
                m_subsectors = new DynamicArray<Subsector>[world.Sectors.Count];

            for (int i = 0; i < world.Sectors.Count; i++)
                m_subsectors[i] = new();

            for (int i = 0; i < world.BspTree.Subsectors.Length; i++)
            {
                var subsector = world.BspTree.Subsectors[i];
                var subsectors = m_subsectors[subsector.Sector.Id];
                subsectors.Add(subsector);
            }

            if (m_drawnSides.Length < world.Sides.Count)
                m_drawnSides = new int[world.Sides.Count];

            const int FloatSize = 4;
            m_lightBufferData = new float[world.Sectors.Count * Constants.LightBuffer.BufferSize * FloatSize + (Constants.LightBuffer.SectorIndexStart * FloatSize)];
        }

        m_lightBuffer?.Dispose();
        m_lightBuffer = new("Sector lights texture buffer", m_lightBufferData, GlVersion.IsVersionSupported(4, 4));

        for (int i = 0; i < world.Sides.Count; i++)
            m_drawnSides[i] = -1;

        m_fakeContrast = world.Config.Render.FakeContrast;

        Clear(m_tickFraction, true);
        SetRenderCompatibility(world);
        CacheData(world);

        Portals.UpdateTo(world);
        m_staticCacheGeometryRenderer.UpdateTo(world, m_lightBuffer);
    }

    private static void ZeroArray<T>(T[] array) where T : struct
    {
        ref var reference = ref MemoryMarshal.GetArrayDataReference(array);
        Unsafe.InitBlockUnaligned(ref Unsafe.As<T, byte>(ref reference), 0, (uint)(Marshal.SizeOf<T>() * array.Length));
    }

    private LegacyVertex[]?[] UpdateVertexWallLookup(LegacyVertex[]?[] vertices, int sideCount, bool free)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            var data = vertices[i];
            if (data == null)
                continue;

            if (free)
            {
                m_world.DataCache.FreeWallVertices(data);
                vertices[i] = null;
                continue;
            }

            ZeroArray(data);
        }

        if (vertices.Length < sideCount)
            return new LegacyVertex[sideCount][];
        return vertices;
    }

    private SkyGeometryVertex[]?[] UpdateSkyWallLookup(SkyGeometryVertex[]?[] vertices, int sideCount, bool free)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            var data = vertices[i];
            if (data == null)
                continue;

            if (free)
            {
                m_world.DataCache.FreeSkyWallVertices(data);
                vertices[i] = null;
                continue;
            }

            ZeroArray(data);
        }

        if (vertices.Length < sideCount)
            return new SkyGeometryVertex[sideCount][];
        return vertices;
    }

    private static void UpdateFlatVertices(DynamicArray<LegacyVertex[][]?> data, int sectorCount, bool free)
    {
        for (int i = 0; i < data.Capacity; i++)
        {
            var lookup = data[i];
            if (lookup == null)
                continue;
            for (int j = 0; j < lookup.Length; j++)
            {
                var vertices = lookup[j];
                if (vertices == null)
                    continue;

                if (free)
                {
                    lookup[j] = null!;
                    continue;
                }

                ZeroArray(vertices);
            }

            if (lookup.Length < sectorCount)
                data[i] = new LegacyVertex[sectorCount][];
        }
    }

    private static void UpdateSkyFlatVertices(DynamicArray<SkyGeometryVertex[][]?> data, int sectorCount, bool free)
    {
        for (int i = 0; i < data.Capacity; i++)
        {
            var lookup = data[i];
            if (lookup == null)
                continue;
            for (int j = 0; j < lookup.Length; j++)
            {
                var vertices = lookup[j];
                if (vertices == null)
                    continue;

                if (free)
                {
                    lookup[j] = null!;
                    continue;
                }

                ZeroArray(vertices);
            }

            if (lookup.Length < sectorCount)
                data[i] = new SkyGeometryVertex[sectorCount][];
        }
    }

    private void SetRenderCompatibility(IWorld world)
    {
        var def = world.Map.CompatibilityDefinition;
        if (def == null)
            return;

        foreach (var sectorId in def.NoRenderFloorSectors)
        {
            if (world.IsSectorIdValid(sectorId))
                world.Sectors[sectorId].Floor.NoRender = true;
        }

        foreach (var sectorId in def.NoRenderCeilingSectors)
        {
            if (world.IsSectorIdValid(sectorId))
                world.Sectors[sectorId].Ceiling.NoRender = true;
        }

        m_midTextureHack.Apply(world, def.MidTextureHackSectors, m_glTextureManager, this);
    }

    private void CacheData(IWorld world)
    {
        Vec2D pos = m_viewPosition.XY;
        bool flood = m_alwaysFlood || m_vanillaFlood;
        foreach (var sector in world.Sectors)
            sector.Flood = flood && world.Geometry.IslandGeometry.FloodSectors.Contains(sector.Id);

        foreach (var subsector in world.BspTree.Subsectors)
            subsector.Flood = flood && world.Geometry.IslandGeometry.BadSubsectors.Contains(subsector.Id);
    }

    public void Clear(double tickFraction, bool newTick)
    {
        m_tickFraction = tickFraction;
        if (newTick)
            m_skyRenderer.Clear();
        Portals.Clear();
        m_lineDrawnTracker.ClearDrawnLines();
        AlphaSides.Clear();
    }
    public void RenderStaticGeometry() =>
        m_staticCacheGeometryRenderer.Render();

    public void RenderPortalsAndSkies(RenderInfo renderInfo)
    {
        m_skyRenderer.Render(renderInfo);
        Portals.Render(renderInfo);
        m_staticCacheGeometryRenderer.RenderSkies(renderInfo);
    }

    public void RenderSector(Sector viewSector, Sector sector, in Vec3D viewPosition, in Vec3D prevViewPosition)
    {
        m_buffer = true;
        m_viewSector = viewSector;
        m_viewPosition = viewPosition;
        m_prevViewPosition = prevViewPosition;

        SetSectorRendering(sector);
        m_transferHeightsView = TransferHeights.GetView(m_viewSector, viewPosition.Z);

        if (sector.TransferHeights != null)
        {
            RenderSectorWalls(sector, viewPosition.XY, prevViewPosition.XY);
            if (!sector.AreFlatsStatic)
                RenderSectorFlats(sector, sector.GetRenderSector(m_transferHeightsView), sector.TransferHeights.ControlSector);
            return;
        }

        m_cacheOverride = false;

        RenderSectorWalls(sector, viewPosition.XY, prevViewPosition.XY);
        if (!sector.AreFlatsStatic)
            RenderSectorFlats(sector, sector, sector);
    }

    public void RenderSectorWall(Sector viewSector, Sector sector, Line line, Vec3D viewPosition, Vec3D prevViewPosition)
    {
        m_buffer = true;
        m_viewSector = viewSector;
        m_viewPosition = viewPosition;
        m_prevViewPosition = prevViewPosition;
        SetSectorRendering(sector);
        RenderSectorSideWall(sector, line.Front, viewPosition.XY, true);
        if (line.Back != null)
            RenderSectorSideWall(sector, line.Back, viewPosition.XY, false);
    }

    private void SetSectorRendering(Sector sector)
    {
        m_transferHeightsView = TransferHeights.GetView(m_viewSector, m_viewPosition.Z);
        if (sector.TransferHeights != null)
        {
            m_floorChanged = m_floorChanged || sector.TransferHeights.ControlSector.Floor.CheckRenderingChanged();
            m_ceilingChanged = m_ceilingChanged || sector.TransferHeights.ControlSector.Ceiling.CheckRenderingChanged();

            // Walls can only cache if middle view
            m_cacheOverride = m_transferHeightsView != TransferHeightView.Middle;
            return;
        }

        m_floorChanged = sector.Floor.CheckRenderingChanged();
        m_ceilingChanged = sector.Ceiling.CheckRenderingChanged();
        m_cacheOverride = false;
    }

    public void SetInitRender()
    {
        SetTransferHeightView(TransferHeightView.Middle);
        SetViewSector(DefaultSector);
        SetBuffer(false);
        m_floorChanged = true;
        m_ceilingChanged = true;
    }

    // The set sector is optional for the transfer heights control sector.
    // This is so the LastRenderGametick can be set for both the sector and transfer heights sector.
    private void RenderSectorFlats(Sector sector, Sector renderSector, Sector set)
    {
        DynamicArray<Subsector> subsectors = m_subsectors[sector.Id];
        sector.LastRenderGametick = m_world.Gametick;

        double floorZ = renderSector.Floor.Z;
        double ceilingZ = renderSector.Ceiling.Z;

        bool floorVisible = m_viewPosition.Z >= floorZ || m_prevViewPosition.Z >= floorZ;
        bool ceilingVisible = m_viewPosition.Z <= ceilingZ || m_prevViewPosition.Z <= ceilingZ;
        if (floorVisible && !sector.IsFloorStatic)
        {
            sector.Floor.LastRenderGametick = m_world.Gametick;
            set.Floor.LastRenderGametick = m_world.Gametick;
            RenderFlat(subsectors, renderSector.Floor, true, out _, out _);
        }
        if (ceilingVisible && !sector.IsCeilingStatic)
        {
            sector.Ceiling.LastRenderGametick = m_world.Gametick;
            set.Ceiling.LastRenderGametick = m_world.Gametick;
            RenderFlat(subsectors, renderSector.Ceiling, false, out _, out _);
        }
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    private void PreloadAllTextures(IWorld world)
    {
        if (world.SameAsPreviousMap)
            return;

        HashSet<int> textures = [];
        for (int i = 0; i < world.Lines.Count; i++)
        {
            var line = world.Lines[i];
            AddSideTextures(textures, line.Front);

            if (line.Back == null)
                continue;

            AddSideTextures(textures, line.Back);
        }

        for (int i = 0; i < world.Sectors.Count; i++)
        {
            textures.Add(world.Sectors[i].Floor.TextureHandle);
            textures.Add(world.Sectors[i].Ceiling.TextureHandle);
        }

        TextureManager.LoadTextureImages(textures);
    }

    private static void AddSideTextures(HashSet<int> textures, Side side)
    {
        textures.Add(side.Lower.TextureHandle);
        textures.Add(side.Middle.TextureHandle);
        textures.Add(side.Upper.TextureHandle);
    }

    private void RenderSectorWalls(Sector sector, Vec2D pos2D, Vec2D prevPos2D)
    {
        for (int i = 0; i < sector.Lines.Count; i++)
        {
            Line line = sector.Lines[i];
            bool onFront = line.Segment.OnRight(pos2D);
            bool onBothSides = onFront != line.Segment.OnRight(prevPos2D);

            if (line.Back != null)
                CheckFloodFillLine(line.Front, line.Back);

            // Need to force render for alternative flood fill from the front side.
            if (onFront || onBothSides || line.Front.LowerFloodKeys.Key2 > 0 || line.Front.UpperFloodKeys.Key2 > 0)
                RenderSectorSideWall(sector, line.Front, pos2D, true);
            // Need to force render for alternative flood fill from the back side.
            if (line.Back != null && (!onFront || onBothSides || line.Back.LowerFloodKeys.Key2 > 0 || line.Back.UpperFloodKeys.Key2 > 0))
                RenderSectorSideWall(sector, line.Back, pos2D, false);
        }
    }

    private void CheckFloodFillLine(Side front, Side back)
    {
        const RenderChangeOptions Options = RenderChangeOptions.None;
        if (front.IsDynamic && m_drawnSides[front.Id] != WorldStatic.CheckCounter &&
            (back.Sector.CheckRenderingChanged(m_world.Gametick, Options) ||
            front.Sector.CheckRenderingChanged(m_world.Gametick, Options)))
            m_staticCacheGeometryRenderer.CheckForFloodFill(front, back,
                front.Sector.GetRenderSector(m_transferHeightsView), back.Sector.GetRenderSector(m_transferHeightsView), isFront: true);

        if (back.IsDynamic && m_drawnSides[back.Id] != WorldStatic.CheckCounter &&
            (front.Sector.CheckRenderingChanged(m_world.Gametick, Options) ||
            back.Sector.CheckRenderingChanged(m_world.Gametick, Options)))
            m_staticCacheGeometryRenderer.CheckForFloodFill(back, front,
                back.Sector.GetRenderSector(m_transferHeightsView), front.Sector.GetRenderSector(m_transferHeightsView), isFront: false);
    }

    private void RenderSectorSideWall(Sector sector, Side side, Vec2D pos2D, bool onFrontSide)
    {
        if (m_drawnSides[side.Id] == WorldStatic.CheckCounter)
            return;

        m_drawnSides[side.Id] = WorldStatic.CheckCounter;
        if (m_config.Render.TextureTransparency && side.Line.Alpha < 1)
        {
            var lineCenter = side.Line.Segment.FromTime(0.5);
            double dx = Math.Max(lineCenter.X - pos2D.X, Math.Max(0, pos2D.X - lineCenter.X));
            double dy = Math.Max(lineCenter.Y - pos2D.Y, Math.Max(0, pos2D.Y - lineCenter.Y));
            side.RenderDistanceSquared = dx * dx + dy * dy;
            AlphaSides.Add(side);
        }

        bool transferHeights = false;
        // Transfer heights has to be drawn by the transfer heights sector
        if (side.Sector.TransferHeights != null &&
            (sector.TransferHeights == null || !ReferenceEquals(sector.TransferHeights.ControlSector, side.Sector.TransferHeights.ControlSector)))
        {
            SetSectorRendering(side.Sector);
            transferHeights = true;
        }

        if (side.IsDynamic)
            RenderSide(side, onFrontSide);

        // Restore to original sector
        if (transferHeights)
            SetSectorRendering(sector);
    }

    public void RenderAlphaSide(Side side, bool isFrontSide)
    {
        if (side.Line.Back == null)
            return;

        if (side.Middle.TextureHandle != Constants.NoTextureIndex)
        {
            Side otherSide = side.PartnerSide!;
            m_cacheOverride = false;
            m_sectorChangedLine = false;
            m_transferHeightsView = TransferHeights.GetView(m_viewSector, m_viewPosition.Z);

            // Only cache if middle view. This can cause sides to be incorrectly cached for upper/lower views and will get used for the middle.
            if (side.Sector.TransferHeights != null || otherSide.Sector.TransferHeights != null)
                m_cacheOverride = m_transferHeightsView != TransferHeightView.Middle;

            if (!m_cacheOverride)
                m_sectorChangedLine = otherSide.Sector.CheckRenderingChanged(side.LastRenderGametickAlpha) || side.Sector.CheckRenderingChanged(side.LastRenderGametickAlpha);

            Sector facingSector = side.Sector.GetRenderSector(m_transferHeightsView);
            Sector otherSector = otherSide.Sector.GetRenderSector(m_transferHeightsView);
            RenderTwoSidedMiddle(side, side.PartnerSide!, facingSector, otherSector, isFrontSide, out _);
            side.LastRenderGametickAlpha = m_world.Gametick;
        }
    }

    public void RenderSide(Side side, bool isFrontSide)
    {
        m_skyOverride = false;

        if (side.FloorFloodKey > 0)
            Portals.UpdateFloodFillPlane(side, side.Sector, SectorPlanes.Floor, SectorPlaneFace.Floor, isFrontSide);
        if (side.CeilingFloodKey > 0)
            Portals.UpdateFloodFillPlane(side, side.Sector, SectorPlanes.Ceiling, SectorPlaneFace.Ceiling, isFrontSide);

        if (side.Line.Flags.TwoSided && side.Line.Back != null)
            RenderTwoSided(side, isFrontSide);
        else if (side.IsDynamic)
            RenderOneSided(side, isFrontSide, out _, out _);
    }

    public void RenderOneSided(Side side, bool isFront, out LegacyVertex[]? vertices, out SkyGeometryVertex[]? skyVertices)
    {
        m_sectorChangedLine = side.Sector.CheckRenderingChanged(side.LastRenderGametick);
        side.LastRenderGametick = m_world.Gametick;

        WallVertices wall = default;
        GLLegacyTexture texture = m_glTextureManager.GetTexture(side.Middle.TextureHandle);
        LegacyVertex[]? data = m_vertexLookup[side.Id];

        var renderSector = side.Sector.GetRenderSector(m_transferHeightsView);

        SectorPlane floor = renderSector.Floor;
        SectorPlane ceiling = renderSector.Ceiling;
        RenderSkySide(side, renderSector, null, texture, out skyVertices);

        if (side.Middle.TextureHandle <= Constants.NullCompatibilityTextureIndex)
        {
            vertices = null;
            return;
        }

        if (side.OffsetChanged || m_sectorChangedLine || data == null || m_cacheOverride)
        {
            int lightIndex = StaticCacheGeometryRenderer.GetLightBufferIndex(renderSector, LightBufferType.Wall);
            WorldTriangulator.HandleOneSided(side, floor, ceiling, texture.UVInverse, ref wall, isFront: isFront);
            if (m_cacheOverride)
            {
                data = m_wallVertices;
                SetWallVertices(data, wall, GetLightLevelAdd(side), lightIndex);
            }
            else if (data == null)
                data = GetWallVertices(wall, GetLightLevelAdd(side), lightIndex);
            else
                SetWallVertices(data, wall, GetLightLevelAdd(side), lightIndex);

            if (!m_cacheOverride)
                m_vertexLookup[side.Id] = data;
        }

        if (m_buffer)
        {
            RenderWorldData renderData = m_worldDataManager.GetRenderData(texture, m_program);
            renderData.Vbo.Add(data);
        }
        vertices = data;
    }

    private int GetLightLevelAdd(Side side)
    {
        if (!m_fakeContrast)
            return 0;

        if (side.Line.StartPosition.Y == side.Line.EndPosition.Y)
            return -16;
        else if (side.Line.StartPosition.X == side.Line.EndPosition.X)
            return 16;

        return 0;
    }

    public void SetRenderOneSided(Side side)
    {
        m_sectorChangedLine = side.Sector.CheckRenderingChanged(side.LastRenderGametick);
    }

    public void SetRenderTwoSided(Side facingSide)
    {
        Side otherSide = facingSide.PartnerSide!;
        m_sectorChangedLine = otherSide.Sector.CheckRenderingChanged(facingSide.LastRenderGametick) || facingSide.Sector.CheckRenderingChanged(facingSide.LastRenderGametick);
    }

    public void SetRenderFloor(SectorPlane floor)
    {
        floor = floor.Sector.GetRenderSector(TransferHeightView.Middle).Floor;
        m_floorChanged = floor.CheckRenderingChanged();
    }

    public void SetRenderCeiling(SectorPlane ceiling)
    {
        ceiling = ceiling.Sector.GetRenderSector(TransferHeightView.Middle).Ceiling;
        m_ceilingChanged = ceiling.CheckRenderingChanged();
    }

    private void RenderTwoSided(Side facingSide, bool isFrontSide)
    {
        Side otherSide = facingSide.PartnerSide!;
        Sector facingSector = facingSide.Sector.GetRenderSector(m_transferHeightsView);
        Sector otherSector = otherSide.Sector.GetRenderSector(m_transferHeightsView);

        m_sectorChangedLine = otherSide.Sector.CheckRenderingChanged(facingSide.LastRenderGametick) || facingSide.Sector.CheckRenderingChanged(facingSide.LastRenderGametick);
        facingSide.LastRenderGametick = m_world.Gametick;
        if (facingSide.IsDynamic && LowerIsVisible(facingSide, facingSector, otherSector))
            RenderTwoSidedLower(facingSide, otherSide, facingSector, otherSector, isFrontSide, out _, out _);
        if ((!m_config.Render.TextureTransparency || facingSide.Line.Alpha >= 1) && facingSide.Middle.TextureHandle != Constants.NoTextureIndex &&
            facingSide.IsDynamic)
            RenderTwoSidedMiddle(facingSide, otherSide, facingSector, otherSector, isFrontSide, out _);
        if (facingSide.IsDynamic && UpperOrSkySideIsVisible(TextureManager, facingSide, facingSector, otherSector, out _))
            RenderTwoSidedUpper(facingSide, otherSide, facingSector, otherSector, isFrontSide, out _, out _, out _);
    }

    public static bool LowerIsVisible(Side facingSide, Sector facingSector, Sector otherSector)
    {
        return facingSector.Floor.Z < otherSector.Floor.Z || facingSector.Floor.PrevZ < otherSector.Floor.PrevZ ||
            facingSide.LowerFloodKeys.Key1 > 0;
    }

    public static bool UpperIsVisible(Side facingSide, Side otherSide, Sector facingSector, Sector otherSector)
    {
        return facingSector.Ceiling.Z > otherSector.Ceiling.Z || facingSector.Ceiling.PrevZ > otherSector.Ceiling.PrevZ ||
            facingSide.UpperFloodKeys.Key1 > 0;
    }

    public static bool UpperIsVisibleOrFlood(TextureManager textureManager, Side facingSide, Side otherSide, Sector facingSector, Sector otherSector)
    {
        bool isSky = textureManager.IsSkyTexture(facingSector.Ceiling.TextureHandle);
        bool isOtherSky = textureManager.IsSkyTexture(otherSector.Ceiling.TextureHandle);

        bool upperVisible = GeometryRenderer.UpperOrSkySideIsVisible(textureManager, facingSide, facingSector, otherSector, out bool skyHack);
        if (!upperVisible && !skyHack && !isOtherSky && isSky)
            return true;

        return upperVisible;
    }

    public static bool UpperOrSkySideIsVisible(TextureManager textureManager, Side facingSide, Sector facingSector, Sector otherSector, out bool skyHack)
    {
        skyHack = false;
        double facingZ = facingSector.Ceiling.Z;
        double otherZ = otherSector.Ceiling.Z;
        double prevFacingZ = facingSector.Ceiling.PrevZ;
        double prevOtherZ = otherSector.Ceiling.PrevZ;
        bool isFacingSky = textureManager.IsSkyTexture(facingSector.Ceiling.TextureHandle);
        bool isOtherSky = textureManager.IsSkyTexture(otherSector.Ceiling.TextureHandle);

        if (isFacingSky && isOtherSky)
        {
            // The sky is only drawn if there is no opening height
            // Otherwise ignore this line for sky effects
            skyHack = LineOpening.GetOpeningHeight(facingSide.Line) <= 0 && facingZ != otherZ;
            return skyHack;
        }

        bool upperVisible = facingZ > otherZ || prevFacingZ > prevOtherZ;
        // Return true if the upper is not visible so DrawTwoSidedUpper can attempt to draw sky hacks
        if (isFacingSky)
        {
            if ((facingSide.FloodTextures & SideTexture.Upper) != 0)
                return true;

            if (facingSide.Upper.TextureHandle == Constants.NoTextureIndex)
            {
                skyHack = facingZ <= otherZ || prevFacingZ <= prevOtherZ;
                return skyHack;
            }

            // Need to draw sky upper if other sector is not sky.
            skyHack = !isOtherSky;
            return skyHack;
        }

        return upperVisible;
    }

    public void RenderTwoSidedLower(Side facingSide, Side otherSide, Sector facingSector, Sector otherSector, bool isFrontSide,
        out LegacyVertex[]? vertices, out SkyGeometryVertex[]? skyVertices)
    {
        Wall lowerWall = facingSide.Lower;
        WallVertices wall = default;
        bool isSky = TextureManager.IsSkyTexture(otherSide.Sector.Floor.TextureHandle) && lowerWall.TextureHandle == Constants.NoTextureIndex;
        bool skyRender = isSky && TextureManager.IsSkyTexture(otherSide.Sector.Floor.TextureHandle);

        if (facingSide.LowerFloodKeys.Key1 > 0 || facingSide.LowerFloodKeys.Key2 > 0)
        {
            vertices = null;
            skyVertices = null;
            Portals.UpdateStaticFloodFillSide(facingSide, otherSide, otherSector, SideTexture.Lower, isFrontSide);
            // Key2 is used for partner side flood. Still may need to draw the lower.
            if (facingSide.LowerFloodKeys.Key1 > 0)
                return;
        }

        if (facingSide.Sector.FloodOpposingFloor && otherSide.LowerFloodKeys.Key2 > 0)
        {
            Portals.ClearStaticWall(otherSide.LowerFloodKeys.Key2);
            otherSide.LowerFloodKeys.Key2 = 0;
        }

        if (lowerWall.TextureHandle == Constants.NoTextureIndex && !skyRender)
        {
            vertices = null;
            skyVertices = null;
            return;
        }

        GLLegacyTexture texture = m_glTextureManager.GetTexture(lowerWall.TextureHandle);
        RenderWorldData renderData = m_worldDataManager.GetRenderData(texture, m_program);

        SectorPlane top = otherSector.Floor;
        SectorPlane bottom = facingSector.Floor;

        if (isSky)
        {
            SkyGeometryVertex[]? data = m_skyWallVertexLowerLookup[facingSide.Id];

            if (facingSide.OffsetChanged || m_sectorChangedLine || data == null)
            {
                WorldTriangulator.HandleTwoSidedLower(facingSide, top, bottom, texture.UVInverse, isFrontSide, ref wall);
                if (data == null)
                    data = CreateSkyWallVertices(wall);
                else
                    SetSkyWallVertices(data, wall);
                m_skyWallVertexLowerLookup[facingSide.Id] = data;
            }

            m_skyRenderer.Add(data, data.Length, otherSide.Sector.SkyTextureHandle, otherSide.Sector.FlipSkyTexture);
            vertices = null;
            skyVertices = data;
        }
        else
        {
            LegacyVertex[]? data = m_vertexLowerLookup[facingSide.Id];

            if (facingSide.OffsetChanged || m_sectorChangedLine || data == null || m_cacheOverride)
            {
                int lightIndex = StaticCacheGeometryRenderer.GetLightBufferIndex(facingSector, LightBufferType.Wall);
                // This lower would clip into the upper texture. Pick the upper as the priority and stop at the ceiling.
                if (top.Z > otherSector.Ceiling.Z && !TextureManager.IsSkyTexture(otherSector.Ceiling.TextureHandle))
                    top = otherSector.Ceiling;

                WorldTriangulator.HandleTwoSidedLower(facingSide, top, bottom, texture.UVInverse, isFrontSide, ref wall);
                if (m_cacheOverride)
                {
                    data = m_wallVertices;
                    SetWallVertices(data, wall, GetLightLevelAdd(facingSide), lightIndex);
                }
                else if (data == null)
                    data = GetWallVertices(wall, GetLightLevelAdd(facingSide), lightIndex);
                else
                    SetWallVertices(data, wall, GetLightLevelAdd(facingSide), lightIndex);

                if (!m_cacheOverride)
                    m_vertexLowerLookup[facingSide.Id] = data;
            }

            // See RenderOneSided() for an ASCII image of why we do this.
            if (m_buffer)
                renderData.Vbo.Add(data);
            vertices = data;
            skyVertices = null;
        }
    }

    public void RenderTwoSidedUpper(Side facingSide, Side otherSide, Sector facingSector, Sector otherSector, bool isFrontSide,
        out LegacyVertex[]? vertices, out SkyGeometryVertex[]? skyVertices, out SkyGeometryVertex[]? skyVertices2)
    {
        SectorPlane plane = otherSector.Ceiling;
        bool isSky = TextureManager.IsSkyTexture(plane.TextureHandle) && TextureManager.IsSkyTexture(facingSector.Ceiling.TextureHandle);
        Wall upperWall = facingSide.Upper;
        bool renderSkySideOnly = false;
        vertices = null;
        skyVertices = null;
        skyVertices2 = null;

        if (facingSide.UpperFloodKeys.Key1 > 0 || facingSide.UpperFloodKeys.Key2 > 0)
        {
            Portals.UpdateStaticFloodFillSide(facingSide, otherSide, otherSector, SideTexture.Upper, isFrontSide);
            // Key2 is used for partner side flood. Still may need to draw the upper.
            // Flood only floods the upper texture portion. If the ceiling is a sky texture then the fake sky side needs to be rendered with RenderSkySide.
            renderSkySideOnly = facingSide.UpperFloodKeys.Key1 > 0;
        }

        if (!TextureManager.IsSkyTexture(facingSector.Ceiling.TextureHandle) &&
            upperWall.TextureHandle == Constants.NoTextureIndex)
        {
            if (TextureManager.IsSkyTexture(otherSector.Ceiling.TextureHandle))
                m_skyOverride = true;
            return;
        }

        if (facingSide.Sector.FloodOpposingCeiling && otherSide.UpperFloodKeys.Key2 > 0)
        {
            Portals.ClearStaticWall(otherSide.UpperFloodKeys.Key2);
            otherSide.UpperFloodKeys.Key2 = 0;
        }

        WallVertices wall = default;
        GLLegacyTexture texture = m_glTextureManager.GetTexture(upperWall.TextureHandle);
        RenderWorldData renderData = m_worldDataManager.GetRenderData(texture, m_program);

        SectorPlane top = facingSector.Ceiling;
        SectorPlane bottom = otherSector.Ceiling;

        RenderSkySide(facingSide, facingSector, otherSector, texture, out skyVertices2);
        if (renderSkySideOnly)
            return;

        if (isSky)
        {
            SkyGeometryVertex[]? data = m_skyWallVertexUpperLookup[facingSide.Id];

            if (TextureManager.IsSkyTexture(otherSide.Sector.Ceiling.TextureHandle))
            {
                m_skyOverride = true;
                vertices = null;
                skyVertices = null;
                return;
            }

            if (facingSide.OffsetChanged || m_sectorChangedLine || data == null)
            {
                WorldTriangulator.HandleTwoSidedUpper(facingSide, top, bottom, texture.UVInverse,
                    isFrontSide, ref wall, MaxSky);
                if (data == null)
                    data = CreateSkyWallVertices(wall);
                else
                    SetSkyWallVertices(data, wall);
                m_skyWallVertexUpperLookup[facingSide.Id] = data;
            }

            m_skyRenderer.Add(data, data.Length, plane.Sector.SkyTextureHandle, plane.Sector.FlipSkyTexture);
            vertices = null;
            skyVertices = data;
        }
        else
        {
            if (facingSide.Upper.TextureHandle == Constants.NoTextureIndex && skyVertices2 != null ||
                !UpperIsVisible(facingSide, otherSide, facingSector, otherSector))
            {
                // This isn't the best spot for this but separating this logic would be difficult. (Sector 72 in skyshowcase.wad)
                vertices = null;
                skyVertices = null;
                return;
            }

            LegacyVertex[]? data = m_vertexUpperLookup[facingSide.Id];

            if (facingSide.OffsetChanged || m_sectorChangedLine || data == null || m_cacheOverride)
            {
                int lightIndex = StaticCacheGeometryRenderer.GetLightBufferIndex(facingSector, LightBufferType.Wall);
                WorldTriangulator.HandleTwoSidedUpper(facingSide, top, bottom, texture.UVInverse, isFrontSide, ref wall);
                if (m_cacheOverride)
                {
                    data = m_wallVertices;
                    SetWallVertices(data, wall, GetLightLevelAdd(facingSide), lightIndex);
                }
                else if (data == null)
                    data = GetWallVertices(wall, GetLightLevelAdd(facingSide), lightIndex);
                else
                    SetWallVertices(data, wall, GetLightLevelAdd(facingSide), lightIndex);

                if (!m_cacheOverride)
                    m_vertexUpperLookup[facingSide.Id] = data;
            }

            // See RenderOneSided() for an ASCII image of why we do this.
            if (m_buffer)
                renderData.Vbo.Add(data);
            vertices = data;
            skyVertices = null;
        }
    }

    private void RenderSkySide(Side facingSide, Sector facingSector, Sector? otherSector, GLLegacyTexture texture, out SkyGeometryVertex[]? skyVertices)
    {
        skyVertices = null;
        if (otherSector == null)
        {
            if (!TextureManager.IsSkyTexture(facingSector.Ceiling.TextureHandle))
                return;
        }
        else
        {
            if (!TextureManager.IsSkyTexture(facingSector.Ceiling.TextureHandle) &&
                !TextureManager.IsSkyTexture(otherSector.Ceiling.TextureHandle))
                return;
        }

        bool isFront = facingSide.IsFront;
        SectorPlane floor = facingSector.Floor;
        SectorPlane ceiling = facingSector.Ceiling;

        WallVertices wall = default;
        if (facingSide.Line.Back != null && otherSector != null && LineOpening.IsRenderingBlocked(facingSide.Line) &&
            SkyUpperRenderFromFloorCheck(facingSide, facingSector, otherSector))
        {
            WorldTriangulator.HandleOneSided(facingSide, floor, ceiling, texture.UVInverse, ref wall,
                overrideFloor: facingSide.PartnerSide!.Sector.Floor.Z, overrideCeiling: MaxSky, isFront);
        }
        else
        {
            WorldTriangulator.HandleOneSided(facingSide, floor, ceiling, texture.UVInverse, ref wall,
                overrideFloor: facingSector.Ceiling.Z, overrideCeiling: MaxSky, isFront);
        }

        SetSkyWallVertices(m_skyWallVertices, wall);
        m_skyRenderer.Add(m_skyWallVertices, m_skyWallVertices.Length, facingSide.Sector.SkyTextureHandle, facingSide.Sector.FlipSkyTexture);
        skyVertices = m_skyWallVertices;
    }

    public void RenderSkySide(Side facingSide, Sector facingSector, SectorPlaneFace face, bool isFront, out SkyGeometryVertex[]? skyVertices)
    {
        WallVertices wall = default;
        if (face == SectorPlaneFace.Floor)
        {
            WorldTriangulator.HandleOneSided(facingSide, facingSector.Floor, facingSector.Ceiling, Vec2F.Zero, ref wall,
                overrideFloor: facingSector.Floor.Z - MaxSky, overrideCeiling: facingSector.Floor.Z, isFront: isFront);
        }
        else
        {
            WorldTriangulator.HandleOneSided(facingSide, facingSector.Floor, facingSector.Ceiling, Vec2F.Zero, ref wall,
                overrideFloor: facingSector.Ceiling.Z, overrideCeiling: facingSector.Ceiling.Z + MaxSky, isFront: isFront);
        }

        SetSkyWallVertices(m_skyWallVertices, wall);
        skyVertices = m_skyWallVertices;
    }

    private bool SkyUpperRenderFromFloorCheck(Side facingSide, Sector facingSector, Sector otherSector)
    {
        if (facingSide.Upper.TextureHandle == Constants.NoTextureIndex && facingSide.UpperFloodKeys.Key1 == 0)
            return true;

        if (TextureManager.IsSkyTexture(facingSector.Ceiling.TextureHandle) &&
            TextureManager.IsSkyTexture(otherSector.Ceiling.TextureHandle))
            return true;

        return false;
    }

    public void RenderTwoSidedMiddle(Side facingSide, Side otherSide, Sector facingSector, Sector otherSector, bool isFrontSide,
        out LegacyVertex[]? vertices)
    {
        Wall middleWall = facingSide.Middle;
        GLLegacyTexture texture = m_glTextureManager.GetTexture(middleWall.TextureHandle, repeatY: false);

        float alpha = m_config.Render.TextureTransparency ? facingSide.Line.Alpha : 1.0f;
        LegacyVertex[]? data = m_vertexLookup[facingSide.Id];
        RenderWorldData renderData = alpha < 1 ?
            m_worldDataManager.GetAlphaRenderData(texture, m_program) :
            m_worldDataManager.GetRenderData(texture, m_program);

        if (facingSide.OffsetChanged || m_sectorChangedLine || data == null || m_cacheOverride)
        {
            (double bottomZ, double topZ) = FindOpeningFlats(facingSector, otherSector);
            (double prevBottomZ, double prevTopZ) = FindOpeningFlatsPrev(facingSector, otherSector);
            double offset = GetTransferHeightHackOffset(facingSide, otherSide, bottomZ, topZ, previous: false);
            double prevOffset = 0;

            if (offset != 0)
                prevOffset = GetTransferHeightHackOffset(facingSide, otherSide, bottomZ, topZ, previous: true);

            int lightIndex = StaticCacheGeometryRenderer.GetLightBufferIndex(facingSector, LightBufferType.Wall);
            // Not going to do anything with out nothingVisible for now
            WallVertices wall = default;
            WorldTriangulator.HandleTwoSidedMiddle(facingSide,
                texture.Dimension, texture.UVInverse, bottomZ, topZ, prevBottomZ, prevTopZ, isFrontSide, ref wall, out _, offset, prevOffset);

            if (m_cacheOverride)
            {
                data = m_wallVertices;
                SetWallVertices(data, wall, GetLightLevelAdd(facingSide), lightIndex, alpha, addAlpha: 0);
            }
            else if (data == null)
                data = GetWallVertices(wall, GetLightLevelAdd(facingSide), lightIndex, alpha, addAlpha: 0);
            else
                SetWallVertices(data, wall, GetLightLevelAdd(facingSide), lightIndex, alpha, addAlpha: 0);

            if (!m_cacheOverride)
                m_vertexLookup[facingSide.Id] = data;
        }

        // See RenderOneSided() for an ASCII image of why we do this.
        if (m_buffer)
            renderData.Vbo.Add(data);
        vertices = data;
    }

    // There is some issue with how the original code renders middle textures with transfer heights.
    // It appears to incorrectly draw from the floor of the original sector instead of the transfer heights sector.
    // Alternatively, I could be dumb and this is dumb but it appears to work.
    private double GetTransferHeightHackOffset(Side facingSide, Side otherSide, double bottomZ, double topZ, bool previous)
    {
        if (otherSide.Sector.TransferHeights == null && facingSide.Sector.TransferHeights == null)
            return 0;

        (double originalBottomZ, double originalTopZ) = previous ?
            FindOpeningFlatsPrev(facingSide.Sector, otherSide.Sector) :
            FindOpeningFlats(facingSide.Sector, otherSide.Sector);

        if (facingSide.Line.Flags.Unpegged.Lower)
            return originalBottomZ - bottomZ;

        return originalTopZ - topZ;
    }

    public static (double bottomZ, double topZ) FindOpeningFlats(Sector facingSector, Sector otherSector)
    {
        SectorPlane facingFloor = facingSector.Floor;
        SectorPlane facingCeiling = facingSector.Ceiling;
        SectorPlane otherFloor = otherSector.Floor;
        SectorPlane otherCeiling = otherSector.Ceiling;

        double facingFloorZ = facingFloor.Z;
        double facingCeilingZ = facingCeiling.Z;
        double otherFloorZ = otherFloor.Z;
        double otherCeilingZ = otherCeiling.Z;

        double bottomZ = facingFloorZ;
        double topZ = facingCeilingZ;
        if (otherFloorZ > facingFloorZ)
            bottomZ = otherFloorZ;
        if (otherCeilingZ < facingCeilingZ)
            topZ = otherCeilingZ;

        return (bottomZ, topZ);
    }

    public static (double bottomZ, double topZ) FindOpeningFlatsPrev(Sector facingSector, Sector otherSector)
    {
        SectorPlane facingFloor = facingSector.Floor;
        SectorPlane facingCeiling = facingSector.Ceiling;
        SectorPlane otherFloor = otherSector.Floor;
        SectorPlane otherCeiling = otherSector.Ceiling;

        double facingFloorZ = facingFloor.PrevZ;
        double facingCeilingZ = facingCeiling.PrevZ;
        double otherFloorZ = otherFloor.PrevZ;
        double otherCeilingZ = otherCeiling.PrevZ;

        double bottomZ = facingFloorZ;
        double topZ = facingCeilingZ;
        if (otherFloorZ > facingFloorZ)
            bottomZ = otherFloorZ;
        if (otherCeilingZ < facingCeilingZ)
            topZ = otherCeilingZ;

        return (bottomZ, topZ);
    }

    public void SetTransferHeightView(TransferHeightView view) => m_transferHeightsView = view;
    public void SetBuffer(bool set) => m_buffer = set;
    public void SetViewSector(Sector sector) => m_viewSector = sector;

    public void RenderSectorFlats(Sector sector, SectorPlane flat, bool floor, out LegacyVertex[]? vertices, out SkyGeometryVertex[]? skyVertices)
    {
        if (sector.Id >= m_subsectors.Length)
        {
            vertices = null;
            skyVertices = null;
            return;
        }

        DynamicArray<Subsector> subsectors = m_subsectors[sector.Id];
        RenderFlat(subsectors, flat, floor, out vertices, out skyVertices);
    }

    private void RenderFlat(DynamicArray<Subsector> subsectors, SectorPlane flat, bool floor, out LegacyVertex[]? vertices, out SkyGeometryVertex[]? skyVertices)
    {
        bool isSky = TextureManager.IsSkyTexture(flat.TextureHandle);
        GLLegacyTexture texture = m_glTextureManager.GetTexture(flat.TextureHandle);
        RenderWorldData renderData = m_worldDataManager.GetRenderData(texture, m_program);
        bool flatChanged = FlatChanged(flat);
        int id = subsectors[0].Sector.Id;
        Sector renderSector = subsectors[0].Sector.GetRenderSector(m_transferHeightsView);
        var textureVector = new Vec2F(texture.Dimension.Vector.X, texture.Dimension.Vector.Y);

        int indexStart = 0;
        if (isSky)
        {
            SkyGeometryVertex[] lookupData = GetSkySectorVertices(subsectors, floor, id, out bool generate);
            if (generate || flatChanged)
            {
                for (int j = 0; j < subsectors.Length; j++)
                {
                    Subsector subsector = subsectors[j];
                    if (floor && subsector.Flood && !flat.MidTextureHack)
                        continue;

                    WorldTriangulator.HandleSubsector(subsector, flat, textureVector, m_subsectorVertices,
                        floor ? flat.Z : MaxSky);
                    TriangulatedWorldVertex root = m_subsectorVertices[0];
                    for (int i = 1; i < m_subsectorVertices.Length - 1; i++)
                    {
                        TriangulatedWorldVertex second = m_subsectorVertices[i];
                        TriangulatedWorldVertex third = m_subsectorVertices[i + 1];
                        CreateSkyFlatVertices(lookupData, indexStart, root, second, third);
                        indexStart += 3;
                    }
                }
            }

            vertices = null;
            skyVertices = lookupData;
            m_skyRenderer.Add(lookupData, lookupData.Length, subsectors[0].Sector.SkyTextureHandle, subsectors[0].Sector.FlipSkyTexture);
        }
        else
        {
            if (m_alwaysFlood)
            {
                vertices = null;
                skyVertices = null;
                return;
            }

            LegacyVertex[] lookupData = GetSectorVertices(subsectors, floor, id, out bool generate);
            if (generate || flatChanged)
            {
                int lightIndex = floor ? StaticCacheGeometryRenderer.GetLightBufferIndex(renderSector, LightBufferType.Floor) :
                            StaticCacheGeometryRenderer.GetLightBufferIndex(renderSector, LightBufferType.Ceiling);
                for (int j = 0; j < subsectors.Length; j++)
                {
                    Subsector subsector = subsectors[j];
                    // Don't ignore transferheights sectors. Flood filling sector flats for transfer heights can't currently be emulated.
                    if (subsector.Flood && !flat.MidTextureHack && subsector.Sector.TransferHeights == null)
                        continue;

                    WorldTriangulator.HandleSubsector(subsector, flat, textureVector, m_subsectorVertices);

                    TriangulatedWorldVertex root = m_subsectorVertices[0];
                    for (int i = 1; i < m_subsectorVertices.Length - 1; i++)
                    {
                        TriangulatedWorldVertex second = m_subsectorVertices[i];
                        TriangulatedWorldVertex third = m_subsectorVertices[i + 1];
                        GetFlatVertices(lookupData, indexStart, ref root, ref second, ref third, lightIndex);
                        indexStart += 3;
                    }
                }
            }

            skyVertices = null;
            vertices = lookupData;
            renderData.Vbo.Add(lookupData);
        }
    }

    private LegacyVertex[] GetSectorVertices(DynamicArray<Subsector> subsectors, bool floor, int id, out bool generate)
    {
        LegacyVertex[][]? lookupView = floor ? m_vertexFloorLookup[(int)m_transferHeightsView] : m_vertexCeilingLookup[(int)m_transferHeightsView];
        if (lookupView == null)
        {
            lookupView ??= new LegacyVertex[m_world.Sectors.Count][];
            if (floor)
                m_vertexFloorLookup[(int)m_transferHeightsView] = lookupView;
            else
                m_vertexCeilingLookup[(int)m_transferHeightsView] = lookupView;
        }

        LegacyVertex[]? data = lookupView[id];
        generate = data == null;
        data ??= InitSectorVertices(subsectors, floor, id, lookupView);
        return data;
    }

    private SkyGeometryVertex[] GetSkySectorVertices(DynamicArray<Subsector> subsectors, bool floor, int id, out bool generate)
    {
        SkyGeometryVertex[][]? lookupView = floor ? m_skyFloorVertexLookup[(int)m_transferHeightsView] : m_skyCeilingVertexLookup[(int)m_transferHeightsView];
        if (lookupView == null)
        {
            lookupView ??= new SkyGeometryVertex[m_world.Sectors.Count][];
            if (floor)
                m_skyFloorVertexLookup[(int)m_transferHeightsView] = lookupView;
            else
                m_skyCeilingVertexLookup[(int)m_transferHeightsView] = lookupView;
        }

        SkyGeometryVertex[]? data = lookupView[id];
        generate = data == null;
        data ??= InitSkyVertices(subsectors, floor, id, lookupView);
        return data;
    }

    private static LegacyVertex[] InitSectorVertices(DynamicArray<Subsector> subsectors, bool floor, int id, LegacyVertex[][] lookup)
    {
        int count = 0;
        for (int j = 0; j < subsectors.Length; j++)
            count += (subsectors[j].ClockwiseEdges.Count - 2) * 3;

        var data = new LegacyVertex[count];
        if (floor)
            lookup[id] = data;
        else
            lookup[id] = data;

        return data;
    }

    private static SkyGeometryVertex[] InitSkyVertices(DynamicArray<Subsector> subsectors, bool floor, int id, SkyGeometryVertex[][] lookup)
    {
        int count = 0;
        for (int j = 0; j < subsectors.Length; j++)
            count += (subsectors[j].ClockwiseEdges.Count - 2) * 3;

        var data = new SkyGeometryVertex[count];
        if (floor)
            lookup[id] = data;
        else
            lookup[id] = data;

        return data;
    }

    private bool FlatChanged(SectorPlane flat)
    {
        if (flat.Facing == SectorPlaneFace.Floor)
            return m_floorChanged;
        else
            return m_ceilingChanged;
    }

    private static unsafe void SetSkyWallVertices(SkyGeometryVertex[] data, in WallVertices wv)
    {
        fixed (SkyGeometryVertex* startVertex = &data[0])
        {
            SkyGeometryVertex* vertex = startVertex;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.TopLeft.Z;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.BottomRight.Z;
        }
    }

    private static unsafe SkyGeometryVertex[] CreateSkyWallVertices(in WallVertices wv)
    {
        var data = WorldStatic.DataCache.GetSkyWallVertices();
        fixed (SkyGeometryVertex* startVertex = &data[0])
        {
            SkyGeometryVertex* vertex = startVertex;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.TopLeft.Z;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.BottomRight.Z;
        }

        return data;
    }

    private static unsafe void CreateSkyFlatVertices(SkyGeometryVertex[] vertices, int startIndex, in TriangulatedWorldVertex root, in TriangulatedWorldVertex second, in TriangulatedWorldVertex third)
    {
        fixed (SkyGeometryVertex* startVertex = &vertices[startIndex])
        {
            SkyGeometryVertex* vertex = startVertex;
            vertex->X = root.X;
            vertex->Y = root.Y;
            vertex->Z = root.Z;

            vertex++;
            vertex->X = second.X;
            vertex->Y = second.Y;
            vertex->Z = second.Z;

            vertex++;
            vertex->X = third.X;
            vertex->Y = third.Y;
            vertex->Z = third.Z;
        }
    }

    private static unsafe void SetWallVertices(LegacyVertex[] data, in WallVertices wv, float lightLevelAdd, int lightBufferIndex,
        float alpha = 1.0f, float addAlpha = 1.0f)
    {
        fixed (LegacyVertex* startVertex = &data[0])
        {
            LegacyVertex* vertex = startVertex;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.TopLeft.Z;
            vertex->PrevX = wv.TopLeft.X;
            vertex->PrevY = wv.TopLeft.Y;
            vertex->PrevZ = wv.PrevTopZ;
            vertex->U = wv.TopLeft.U;
            vertex->V = wv.TopLeft.V;
            vertex->PrevU = wv.TopLeft.PrevU;
            vertex->PrevV = wv.TopLeft.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;
            vertex->PrevX = wv.TopLeft.X;
            vertex->PrevY = wv.TopLeft.Y;
            vertex->PrevZ = wv.PrevBottomZ;
            vertex->U = wv.TopLeft.U;
            vertex->V = wv.BottomRight.V;
            vertex->PrevU = wv.TopLeft.PrevU;
            vertex->PrevV = wv.BottomRight.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;
            vertex->PrevX = wv.BottomRight.X;
            vertex->PrevY = wv.BottomRight.Y;
            vertex->PrevZ = wv.PrevTopZ;
            vertex->U = wv.BottomRight.U;
            vertex->V = wv.TopLeft.V;
            vertex->PrevU = wv.BottomRight.PrevU;
            vertex->PrevV = wv.TopLeft.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;
            vertex->PrevX = wv.BottomRight.X;
            vertex->PrevY = wv.BottomRight.Y;
            vertex->PrevZ = wv.PrevTopZ;
            vertex->U = wv.BottomRight.U;
            vertex->V = wv.TopLeft.V;
            vertex->PrevU = wv.BottomRight.PrevU;
            vertex->PrevV = wv.TopLeft.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;
            vertex->PrevX = wv.TopLeft.X;
            vertex->PrevY = wv.TopLeft.Y;
            vertex->PrevZ = wv.PrevBottomZ;
            vertex->U = wv.TopLeft.U;
            vertex->V = wv.BottomRight.V;
            vertex->PrevU = wv.TopLeft.PrevU;
            vertex->PrevV = wv.BottomRight.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.BottomRight.Z;
            vertex->PrevX = wv.BottomRight.X;
            vertex->PrevY = wv.BottomRight.Y;
            vertex->PrevZ = wv.PrevBottomZ;
            vertex->U = wv.BottomRight.U;
            vertex->V = wv.BottomRight.V;
            vertex->PrevU = wv.BottomRight.PrevU;
            vertex->PrevV = wv.BottomRight.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;
        }
    }

    private static unsafe LegacyVertex[] GetWallVertices(in WallVertices wv, float lightLevelAdd, int lightBufferIndex,
        float alpha = 1.0f, float addAlpha = 1.0f)
    {
        var data = WorldStatic.DataCache.GetWallVertices();
        fixed (LegacyVertex* startVertex = &data[0])
        {
            LegacyVertex* vertex = startVertex;
            // Our triangle is added like:
            //    0--2
            //    | /  3
            //    |/  /|
            //    1  / |
            //      4--5
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.TopLeft.Z;
            vertex->PrevX = wv.TopLeft.X;
            vertex->PrevY = wv.TopLeft.Y;
            vertex->PrevZ = wv.PrevTopZ;
            vertex->U = wv.TopLeft.U;
            vertex->V = wv.TopLeft.V;
            vertex->PrevU = wv.TopLeft.PrevU;
            vertex->PrevV = wv.TopLeft.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;
            vertex->PrevX = wv.TopLeft.X;
            vertex->PrevY = wv.TopLeft.Y;
            vertex->PrevZ = wv.PrevBottomZ;
            vertex->U = wv.TopLeft.U;
            vertex->V = wv.BottomRight.V;
            vertex->PrevU = wv.TopLeft.PrevU;
            vertex->PrevV = wv.BottomRight.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;
            vertex->PrevX = wv.BottomRight.X;
            vertex->PrevY = wv.BottomRight.Y;
            vertex->PrevZ = wv.PrevTopZ;
            vertex->U = wv.BottomRight.U;
            vertex->V = wv.TopLeft.V;
            vertex->PrevU = wv.BottomRight.PrevU;
            vertex->PrevV = wv.TopLeft.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.TopLeft.Z;
            vertex->PrevX = wv.BottomRight.X;
            vertex->PrevY = wv.BottomRight.Y;
            vertex->PrevZ = wv.PrevTopZ;
            vertex->U = wv.BottomRight.U;
            vertex->V = wv.TopLeft.V;
            vertex->PrevU = wv.BottomRight.PrevU;
            vertex->PrevV = wv.TopLeft.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.TopLeft.X;
            vertex->Y = wv.TopLeft.Y;
            vertex->Z = wv.BottomRight.Z;
            vertex->PrevX = wv.TopLeft.X;
            vertex->PrevY = wv.TopLeft.Y;
            vertex->PrevZ = wv.PrevBottomZ;
            vertex->U = wv.TopLeft.U;
            vertex->V = wv.BottomRight.V;
            vertex->PrevU = wv.TopLeft.PrevU;
            vertex->PrevV = wv.BottomRight.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;

            vertex++;
            vertex->X = wv.BottomRight.X;
            vertex->Y = wv.BottomRight.Y;
            vertex->Z = wv.BottomRight.Z;
            vertex->PrevX = wv.BottomRight.X;
            vertex->PrevY = wv.BottomRight.Y;
            vertex->PrevZ = wv.PrevBottomZ;
            vertex->U = wv.BottomRight.U;
            vertex->V = wv.BottomRight.V;
            vertex->PrevU = wv.BottomRight.PrevU;
            vertex->PrevV = wv.BottomRight.PrevV;
            vertex->Alpha = alpha;
            vertex->AddAlpha = addAlpha;
            vertex->LightLevelBufferIndex = lightBufferIndex;
            vertex->LightLevelAdd = lightLevelAdd;
        }

        return data;
    }

    private static unsafe void GetFlatVertices(LegacyVertex[] vertices, int startIndex, ref TriangulatedWorldVertex root, ref TriangulatedWorldVertex second, ref TriangulatedWorldVertex third,
        int lightLevelBufferIndex)
    {
        fixed (LegacyVertex* startVertex = &vertices[startIndex])
        {
            LegacyVertex* vertex = startVertex;
            vertex->X = root.X;
            vertex->Y = root.Y;
            vertex->Z = root.Z;
            vertex->PrevX = root.X;
            vertex->PrevY = root.Y;
            vertex->PrevZ = root.PrevZ;
            vertex->U = root.U;
            vertex->V = root.V;
            vertex->PrevU = root.PrevU;
            vertex->PrevV = root.PrevV;
            vertex->Alpha = 1.0f;
            vertex->LightLevelBufferIndex = lightLevelBufferIndex;

            vertex++;
            vertex->X = second.X;
            vertex->Y = second.Y;
            vertex->Z = second.Z;
            vertex->PrevX = second.X;
            vertex->PrevY = second.Y;
            vertex->PrevZ = second.PrevZ;
            vertex->U = second.U;
            vertex->V = second.V;
            vertex->PrevU = second.PrevU;
            vertex->PrevV = second.PrevV;
            vertex->Alpha = 1.0f;
            vertex->LightLevelBufferIndex = lightLevelBufferIndex;

            vertex++;
            vertex->X = third.X;
            vertex->Y = third.Y;
            vertex->Z = third.Z;
            vertex->PrevX = third.X;
            vertex->PrevY = third.Y;
            vertex->PrevZ = third.PrevZ;
            vertex->U = third.U;
            vertex->V = third.V;
            vertex->PrevU = third.PrevU;
            vertex->PrevV = third.PrevV;
            vertex->Alpha = 1.0f;
            vertex->LightLevelBufferIndex = lightLevelBufferIndex;
        }
    }

    private void ReleaseUnmanagedResources()
    {
        m_staticCacheGeometryRenderer.Dispose();
        m_skyRenderer.Dispose();
        Portals.Dispose();
        if (m_lightBuffer != null)
            m_lightBuffer.Dispose();
    }
}

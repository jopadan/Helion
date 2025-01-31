using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Helion.Maps;
using Helion.Resources.Archives.Entries;
using static Helion.Util.Assertion.Assert;

namespace Helion.Resources.Archives.Iterator;

/// <summary>
/// Performs iteration on an archive in search for maps.
/// </summary>
public class ArchiveMapIterator : IEnumerable<MapEntryCollection>
{
    private static readonly HashSet<string> MapEntryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS",
        "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS",
        "TEXTMAP", "ZNODES", "DIALOGUE", "ENDMAP", "GL_LEVEL", "GL_VERT",
        "GL_SEGS", "GL_SSECT", "GL_NODES", "GL_PVS",
    };

    private static readonly Dictionary<string, PropertyInfo> MapEntryLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BEHAVIOR", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Behavior))! },
        { "BLOCKMAP", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Blockmap))! },
        { "DIALOGUE", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Dialogue))! },
        { "ENDMAP",   typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Endmap))! },
        { "GL_LEVEL", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.GLMap))! },
        { "GL_NODES", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.GLNodes))! },
        { "GL_PVS",   typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.GLPVS))! },
        { "GL_SEGS",  typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.GLSegments))! },
        { "GL_SSECT", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.GLSubsectors))! },
        { "GL_VERT",  typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.GLVertices))! },
        { "LINEDEFS", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Linedefs))! },
        { "NODES",    typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Nodes))! },
        { "REJECT",   typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Reject))! },
        { "SCRIPTS",  typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Scripts))! },
        { "SECTORS",  typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Sectors))! },
        { "SEGS",     typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Segments))! },
        { "SSECTORS", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Subsectors))! },
        { "SIDEDEFS", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Sidedefs))! },
        { "THINGS",   typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Things))! },
        { "TEXTMAP",  typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Textmap))! },
        { "VERTEXES", typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Vertices))! },
        { "ZNODES",   typeof(MapEntryCollection).GetProperty(nameof(MapEntryCollection.Znodes))! },
    };

    private readonly Archive m_archive;
    private MapEntryCollection m_currentMap = new();
    private string m_lastEntryName = "";
    private bool m_makingMap;

    public ArchiveMapIterator(Archive archive)
    {
        m_archive = archive;
    }

    public IEnumerator<MapEntryCollection> GetEnumerator()
    {
        foreach (Entry entry in m_archive.Entries)
        {
            string entryName = entry.Path.Name;

            if (m_makingMap)
            {
                if (IsMapEntry(entryName))
                {
                    TrackMapEntry(entryName, entry);
                }
                else
                {
                    if (m_currentMap.IsValid())
                        yield return m_currentMap;
                    ResetMapTrackingData();
                }
            }
            else if (IsMapEntry(entryName))
            {
                TrackMapEntry(entryName, entry);
                m_currentMap.Name = m_lastEntryName;
                m_makingMap = true;
            }

            m_lastEntryName = entryName;
        }

        // After finishing a directory, we may have a residual map that was
        // at the end that needs to be returned.
        if (m_currentMap.IsValid())
            yield return m_currentMap;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool IsGLBSPMapHeader(string entryName)
    {
        // Unfortunately GLBSP decided it'd allow things like GL_XXXXX
        // where the X's are the map name if it's less/equal to 5 letters.
        if (entryName.Length <= 5 || !entryName.StartsWith("GL_", StringComparison.OrdinalIgnoreCase))
            return false;
        
        // Accept partial map name matches. This currently happens in btsx e2 MAP16C
        // Zdbsp creates the map marker as MAP16 when MAP16C is expected
        var mapName = entryName.AsSpan(3, entryName.Length - 3);
        var currentMapName = m_currentMap.Name.AsSpan();
        return currentMapName.StartsWith(mapName, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsMapEntry(string entryName)
    {
        return IsGLBSPMapHeader(entryName) || MapEntryNames.Contains(entryName);
    }

    private void ResetMapTrackingData()
    {
        m_currentMap = new MapEntryCollection();
        m_makingMap = false;
        m_lastEntryName = string.Empty;
    }

    private void TrackMapEntry(string entryName, Entry entry)
    {
        if (IsGLBSPMapHeader(entryName))
        {
            m_currentMap.GLMap = entry;
            return;
        }

        if (MapEntryLookup.ContainsKey(entryName))
            MapEntryLookup[entryName].SetValue(m_currentMap, entry);
        else
            Fail("Unexpected map entry name (not a map entry)");
    }
}

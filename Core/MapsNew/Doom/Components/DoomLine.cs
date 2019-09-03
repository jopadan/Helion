using Helion.MapsNew.Components;
using Helion.MapsNew.Doom.Components.Types;

namespace Helion.MapsNew.Doom.Components
{
    public class DoomLine : ILine
    {
        public int Id { get; }
        public readonly DoomVertex Start;
        public readonly DoomVertex End;
        public readonly DoomSide Front;
        public readonly DoomSide? Back;
        public readonly DoomLineFlags Flags;
        public readonly DoomLineType LineType;
        public readonly ushort SectorTag;
        
        internal DoomLine(int id, DoomVertex start, DoomVertex end, DoomSide front, DoomSide? back, 
            DoomLineFlags flags, DoomLineType lineType, ushort sectorTag)
        {
            Id = id;
            Start = start;
            End = end;
            Front = front;
            Back = back;
            Flags = flags;
            LineType = lineType;
            SectorTag = sectorTag;
        }

        public IVertex GetStart() => Start;
        public IVertex GetEnd() => End;
        public ISide GetFront() => Front;
        public ISide GetBack() => Back;
    }
}
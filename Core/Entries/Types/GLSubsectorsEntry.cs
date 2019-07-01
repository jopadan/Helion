using Helion.Resources;

namespace Helion.Entries.Types
{
    /// <summary>
    /// An entry for GL subsectors.
    /// </summary>
    public class GLSubsectorsEntry : TextEntry
    {
        public GLSubsectorsEntry(EntryId id, EntryPath path, byte[] data, ResourceNamespace resourceNamespace) :
            base(id, path, data, resourceNamespace)
        {
        }
        
        public override ResourceType GetResourceType() => ResourceType.GLSubsectors;
    }
}
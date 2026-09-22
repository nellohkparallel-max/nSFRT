using System.Collections.Generic;

namespace SFRThelper.Models
{
    public class StructureCreationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> CreatedStructureIds { get; set; }
        public string CompositePeaksId { get; set; }
        public string ValleyId { get; set; }
        public string Ring01Id { get; set; }
        public string Ring13Id { get; set; }
        public string PenumbraShellId { get; set; }
        public string ValleyCoreId { get; set; }
        public string PoiPeakCenterId { get; set; }
        public string PoiValleyCenterId { get; set; }
        public bool RolledBack { get; set; }

        public StructureCreationResult()
        {
            CreatedStructureIds = new List<string>();
        }
    }
}

namespace SFRThelper.Models
{
    public class StructureListItem
    {
        public const string NoneId = "(None)";

        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string DicomType { get; set; }
        public double VolumeCc { get; set; }
        public bool IsHighResolution { get; set; }
        public bool IsEmpty { get; set; }

        public static StructureListItem None()
        {
            return new StructureListItem
            {
                Id = NoneId,
                DisplayName = "(None)",
                DicomType = string.Empty,
                VolumeCc = 0,
                IsHighResolution = false,
                IsEmpty = true
            };
        }

        public bool IsNone
        {
            get { return string.IsNullOrEmpty(Id) || Id == NoneId; }
        }

        public override string ToString()
        {
            return DisplayName ?? Id ?? "(None)";
        }
    }
}

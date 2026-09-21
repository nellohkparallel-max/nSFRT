namespace SFRThelper.Models
{
    public class PlanListItem
    {
        public string Key { get; set; }
        public string CourseId { get; set; }
        public string PlanId { get; set; }
        public string DisplayName { get; set; }
        public bool IsPlanSum { get; set; }
        public bool IsDoseValid { get; set; }
        public double? PrescriptionDoseGy { get; set; }

        public override string ToString()
        {
            return DisplayName ?? PlanId;
        }
    }
}

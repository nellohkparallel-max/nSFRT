using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SFRThelper.Models
{
    public class FeasibilitySummary
    {
        public int EstimatedSphereCount { get; set; }
        public double SphereVolumeCc { get; set; }
        public double TargetVolumeCc { get; set; }
        public double ValidVolumeCc { get; set; }
        public double VolumeFractionPercent { get; set; }
        public double EdgeToEdgeClearanceMm { get; set; }
        public double EdgeToEdgeSiClearanceMm { get; set; }
        public double TargetClearanceMm { get; set; }
        public double ExternalClearanceMm { get; set; }
        public string BodyId { get; set; }
        public double Oar1ClearanceMm { get; set; }
        public double Oar2ClearanceMm { get; set; }
        public string Oar1Id { get; set; }
        public string Oar2Id { get; set; }
        public PackingGeometryMode PackingMode { get; set; }
        public bool VolumeFractionOutOfRange { get; set; }
        public bool SpacingInvalid { get; set; }
        public bool HasValidVolume { get; set; }
        public int BaselineCount { get; set; }
        public int OptimizedCount { get; set; }
        public string MaximizationSummary { get; set; }
        public List<string> Warnings { get; set; }

        public FeasibilitySummary()
        {
            Warnings = new List<string>();
        }

        public string SummaryText
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("Estimated spheres: " + EstimatedSphereCount);
                sb.AppendLine("Peak volume: " + Format(SphereVolumeCc) + " cc");
                sb.AppendLine("Target volume: " + Format(TargetVolumeCc) + " cc");
                sb.AppendLine("V_valid volume: " + Format(ValidVolumeCc) + " cc");
                sb.AppendLine("Volume fraction: " + Format(VolumeFractionPercent) + " %"
                    + (VolumeFractionOutOfRange ? "  [outside 1.0–5.0%]" : "  [within 1.0–5.0%]"));
                sb.AppendLine("Edge-to-edge lateral: " + Format(EdgeToEdgeClearanceMm) + " mm");
                sb.AppendLine("Edge-to-edge SI: " + Format(EdgeToEdgeSiClearanceMm) + " mm");
                sb.AppendLine("Target internal clearance: " + Format(TargetClearanceMm) + " mm");
                sb.AppendLine("Skin / external clearance: " + Format(ExternalClearanceMm) + " mm"
                    + (string.IsNullOrEmpty(BodyId) ? "  (no EXTERNAL)" : "  (" + BodyId + ")"));
                sb.AppendLine("OAR 1 clearance: " + Format(Oar1ClearanceMm) + " mm"
                    + (string.IsNullOrEmpty(Oar1Id) ? "  (none)" : "  (" + Oar1Id + ")"));
                sb.AppendLine("OAR 2 clearance: " + Format(Oar2ClearanceMm) + " mm"
                    + (string.IsNullOrEmpty(Oar2Id) ? "  (none)" : "  (" + Oar2Id + ")"));
                    sb.AppendLine("Packing: " + SphereOptimizerLabel(PackingMode));
                if (!string.IsNullOrEmpty(MaximizationSummary))
                    sb.AppendLine(MaximizationSummary);
                if (!HasValidVolume)
                    sb.AppendLine("No valid interior volume for the current radius and clearances.");
                if (Warnings != null)
                {
                    foreach (var warning in Warnings)
                        sb.AppendLine("Warning: " + warning);
                }
                return sb.ToString().TrimEnd();
            }
        }

        private static string SphereOptimizerLabel(PackingGeometryMode mode)
        {
            if (mode == PackingGeometryMode.FaceCenteredCubic)
                return "FCC";
            if (mode == PackingGeometryMode.HexagonalClosePacking)
                return "HCP";
            return "Simple Cubic";
        }

        private static string Format(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "n/a";
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}

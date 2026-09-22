using System.Collections.Generic;
using System.Globalization;

namespace SFRThelper.Models
{
    public class LatticePackingResult
    {
        public List<SphereModel> Spheres { get; set; }
        public int CandidateCount { get; set; }
        public int RejectedOutsideValid { get; set; }
        public int RejectedCollision { get; set; }
        public PackingGeometryMode PackingMode { get; set; }
        public Point3D AnchorCom { get; set; }
        public double PackingEfficiency { get; set; }
        public string Message { get; set; }
        public int BaselineCount { get; set; }
        public int OptimizedCount { get; set; }
        public int SphereCountGain { get; set; }
        public double ShiftXMm { get; set; }
        public double ShiftYMm { get; set; }
        public double ShiftZMm { get; set; }
        public double MeanBoundaryDistanceMm { get; set; }
        public double Score { get; set; }
        public SphereMaximizationStrategy Strategy { get; set; }
        public bool MaximizationRan { get; set; }

        public LatticePackingResult()
        {
            Spheres = new List<SphereModel>();
        }

        public int SphereCount
        {
            get { return Spheres != null ? Spheres.Count : 0; }
        }

        public string MaximizationSummary
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Found: {0} spheres (baseline: {1}, gain: {2:+0;-#;+0})",
                    OptimizedCount, BaselineCount, SphereCountGain);
            }
        }
    }
}

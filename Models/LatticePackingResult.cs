using System.Collections.Generic;

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

        public LatticePackingResult()
        {
            Spheres = new List<SphereModel>();
        }

        public int SphereCount
        {
            get { return Spheres != null ? Spheres.Count : 0; }
        }
    }
}

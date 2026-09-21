namespace SFRThelper.Models
{
    /// <summary>
    /// Phase-1 DTO: all geometry required for CPU lattice packing, with no VMS types.
    /// V_valid is stored as a rasterized occupancy mask of the contracted target minus expanded OARs.
    /// </summary>
    public class LatticeGeometryContext
    {
        public Point3D CenterOfMass { get; set; }
        public BoundingBox3D TargetBounds { get; set; }
        public BoundingBox3D ValidBounds { get; set; }
        public VoxelMask ValidVolume { get; set; }
        public double TargetVolumeCc { get; set; }
        public double ValidVolumeCc { get; set; }
        public bool TargetIsHighResolution { get; set; }
        public string TargetId { get; set; }
        public string Oar1Id { get; set; }
        public string Oar2Id { get; set; }
        public string BodyId { get; set; }
        public ImageGeometryDto Image { get; set; }
        public LatticeTransform Transform { get; set; }
        public string Message { get; set; }
        public bool IsValid
        {
            get { return ValidVolume != null && !ValidVolume.IsEmpty; }
        }
    }
}

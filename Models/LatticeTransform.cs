namespace SFRThelper.Models
{
    /// <summary>
    /// Maps lattice-local coordinates into patient coordinates.
    /// Origin is the target center of mass; R is a row-major 3x3 rotation (identity by default).
    /// </summary>
    public struct LatticeTransform
    {
        public Point3D Origin { get; set; }

        public double Rxx { get; set; }
        public double Rxy { get; set; }
        public double Rxz { get; set; }
        public double Ryx { get; set; }
        public double Ryy { get; set; }
        public double Ryz { get; set; }
        public double Rzx { get; set; }
        public double Rzy { get; set; }
        public double Rzz { get; set; }

        public static LatticeTransform Identity(Point3D origin)
        {
            return new LatticeTransform
            {
                Origin = origin,
                Rxx = 1, Rxy = 0, Rxz = 0,
                Ryx = 0, Ryy = 1, Ryz = 0,
                Rzx = 0, Rzy = 0, Rzz = 1
            };
        }

        public Point3D ToPatient(double lx, double ly, double lz)
        {
            return new Point3D(
                Origin.X + Rxx * lx + Rxy * ly + Rxz * lz,
                Origin.Y + Ryx * lx + Ryy * ly + Ryz * lz,
                Origin.Z + Rzx * lx + Rzy * ly + Rzz * lz);
        }
    }
}

using System;

namespace SFRThelper.Models
{
    /// <summary>
    /// Maps lattice-local coordinates into patient coordinates.
    /// Origin is the target center of mass; R is a row-major 3x3 rotation.
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
            return FromYawDegrees(origin, 0);
        }

        /// <summary>Rotation about the patient Z (SI) axis to break MLC leaf alignment.</summary>
        public static LatticeTransform FromYawDegrees(Point3D origin, double yawDegrees)
        {
            double rad = yawDegrees * Math.PI / 180.0;
            double c = Math.Cos(rad);
            double s = Math.Sin(rad);
            return new LatticeTransform
            {
                Origin = origin,
                Rxx = c, Rxy = -s, Rxz = 0,
                Ryx = s, Ryy = c,  Ryz = 0,
                Rzx = 0, Rzy = 0,  Rzz = 1
            };
        }

        public Point3D ToPatient(double lx, double ly, double lz)
        {
            return new Point3D(
                Origin.X + Rxx * lx + Rxy * ly + Rxz * lz,
                Origin.Y + Ryx * lx + Ryy * ly + Ryz * lz,
                Origin.Z + Rzx * lx + Rzy * ly + Rzz * lz);
        }

        /// <summary>Translate the lattice origin by a shift in lattice-local coordinates.</summary>
        public LatticeTransform WithLocalShift(double lx, double ly, double lz)
        {
            LatticeTransform t = this;
            t.Origin = ToPatient(lx, ly, lz);
            return t;
        }
    }
}

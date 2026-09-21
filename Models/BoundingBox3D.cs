using System;

namespace SFRThelper.Models
{
    public struct BoundingBox3D
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }

        public double SizeX { get { return MaxX - MinX; } }
        public double SizeY { get { return MaxY - MinY; } }
        public double SizeZ { get { return MaxZ - MinZ; } }

        public double Diagonal
        {
            get
            {
                return Math.Sqrt(SizeX * SizeX + SizeY * SizeY + SizeZ * SizeZ);
            }
        }

        public Point3D Center
        {
            get { return new Point3D((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5, (MinZ + MaxZ) * 0.5); }
        }

        public static BoundingBox3D FromMinMax(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            return new BoundingBox3D
            {
                MinX = minX,
                MinY = minY,
                MinZ = minZ,
                MaxX = maxX,
                MaxY = maxY,
                MaxZ = maxZ
            };
        }

        public BoundingBox3D Expand(double marginMm)
        {
            return FromMinMax(
                MinX - marginMm, MinY - marginMm, MinZ - marginMm,
                MaxX + marginMm, MaxY + marginMm, MaxZ + marginMm);
        }

        public bool Contains(Point3D p)
        {
            return p.X >= MinX && p.X <= MaxX
                && p.Y >= MinY && p.Y <= MaxY
                && p.Z >= MinZ && p.Z <= MaxZ;
        }

        public bool IsEmpty
        {
            get { return SizeX <= 0 || SizeY <= 0 || SizeZ <= 0; }
        }
    }
}

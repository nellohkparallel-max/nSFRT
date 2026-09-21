using System;
using System.Collections;

namespace SFRThelper.Models
{
    /// <summary>
    /// Compact 3D occupancy mask of the geometrically valid sphere-center volume (V_valid).
    /// Used on the CPU thread with zero VMS references.
    /// </summary>
    public sealed class VoxelMask
    {
        private readonly BitArray _bits;

        public double OriginX { get; private set; }
        public double OriginY { get; private set; }
        public double OriginZ { get; private set; }
        public double Dx { get; private set; }
        public double Dy { get; private set; }
        public double Dz { get; private set; }
        public int Nx { get; private set; }
        public int Ny { get; private set; }
        public int Nz { get; private set; }
        public int OccupiedCount { get; private set; }

        public VoxelMask(double originX, double originY, double originZ,
            double dx, double dy, double dz, int nx, int ny, int nz)
        {
            if (nx <= 0 || ny <= 0 || nz <= 0)
                throw new ArgumentOutOfRangeException("VoxelMask dimensions must be positive.");
            if (dx <= 0 || dy <= 0 || dz <= 0)
                throw new ArgumentOutOfRangeException("VoxelMask spacing must be positive.");

            OriginX = originX;
            OriginY = originY;
            OriginZ = originZ;
            Dx = dx;
            Dy = dy;
            Dz = dz;
            Nx = nx;
            Ny = ny;
            Nz = nz;
            _bits = new BitArray(nx * ny * nz);
        }

        public static VoxelMask Empty()
        {
            return new VoxelMask(0, 0, 0, 1, 1, 1, 1, 1, 1);
        }

        public bool IsEmpty
        {
            get { return OccupiedCount == 0; }
        }

        public double VolumeMm3
        {
            get { return OccupiedCount * Dx * Dy * Dz; }
        }

        public double VolumeCc
        {
            get { return VolumeMm3 / 1000.0; }
        }

        public BoundingBox3D Bounds
        {
            get
            {
                return BoundingBox3D.FromMinMax(
                    OriginX, OriginY, OriginZ,
                    OriginX + Nx * Dx, OriginY + Ny * Dy, OriginZ + Nz * Dz);
            }
        }

        public void Set(int ix, int iy, int iz, bool value)
        {
            int index = LinearIndex(ix, iy, iz);
            bool previous = _bits[index];
            if (previous == value)
                return;
            _bits[index] = value;
            OccupiedCount += value ? 1 : -1;
        }

        public bool Get(int ix, int iy, int iz)
        {
            if (ix < 0 || iy < 0 || iz < 0 || ix >= Nx || iy >= Ny || iz >= Nz)
                return false;
            return _bits[LinearIndex(ix, iy, iz)];
        }

        /// <summary>
        /// True when the point lies inside an occupied voxel (strictly within the rasterized V_valid).
        /// </summary>
        public bool Contains(Point3D p)
        {
            return Contains(p.X, p.Y, p.Z);
        }

        public bool Contains(double x, double y, double z)
        {
            int ix = (int)Math.Floor((x - OriginX) / Dx);
            int iy = (int)Math.Floor((y - OriginY) / Dy);
            int iz = (int)Math.Floor((z - OriginZ) / Dz);
            return Get(ix, iy, iz);
        }

        public Point3D VoxelCenter(int ix, int iy, int iz)
        {
            return new Point3D(
                OriginX + (ix + 0.5) * Dx,
                OriginY + (iy + 0.5) * Dy,
                OriginZ + (iz + 0.5) * Dz);
        }

        private int LinearIndex(int ix, int iy, int iz)
        {
            return ix + Nx * (iy + Ny * iz);
        }
    }
}

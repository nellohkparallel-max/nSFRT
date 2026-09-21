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
        private float[] _distMm;

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
            _distMm = null;
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
            _distMm = null;
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

        /// <summary>
        /// Euclidean distance (mm) from a point to the nearest unoccupied voxel / V_valid boundary.
        /// Outside V_valid the distance is 0. Requires <see cref="ComputeDistanceField"/>.
        /// </summary>
        public double DistanceToBoundaryMm(Point3D p)
        {
            return DistanceToBoundaryMm(p.X, p.Y, p.Z);
        }

        public double DistanceToBoundaryMm(double x, double y, double z)
        {
            if (_distMm == null || OccupiedCount == 0)
                return Contains(x, y, z) ? Math.Min(Dx, Math.Min(Dy, Dz)) * 0.5 : 0;

            int ix = (int)Math.Floor((x - OriginX) / Dx);
            int iy = (int)Math.Floor((y - OriginY) / Dy);
            int iz = (int)Math.Floor((z - OriginZ) / Dz);
            if (ix < 0 || iy < 0 || iz < 0 || ix >= Nx || iy >= Ny || iz >= Nz)
                return 0;
            if (!_bits[LinearIndex(ix, iy, iz)])
                return 0;
            return _distMm[LinearIndex(ix, iy, iz)];
        }

        public Point3D DistanceGradient(Point3D p)
        {
            double gx = DistanceToBoundaryMm(p.X + Dx, p.Y, p.Z) - DistanceToBoundaryMm(p.X - Dx, p.Y, p.Z);
            double gy = DistanceToBoundaryMm(p.X, p.Y + Dy, p.Z) - DistanceToBoundaryMm(p.X, p.Y - Dy, p.Z);
            double gz = DistanceToBoundaryMm(p.X, p.Y, p.Z + Dz) - DistanceToBoundaryMm(p.X, p.Y, p.Z - Dz);
            return new Point3D(gx, gy, gz).NormalizedOrZero();
        }

        /// <summary>
        /// Chamfer Euclidean distance transform of occupied voxels to the nearest empty / OOB voxel.
        /// O(N) two-pass 26-neighborhood, distances in millimetres.
        /// </summary>
        public void ComputeDistanceField()
        {
            int n = Nx * Ny * Nz;
            _distMm = new float[n];
            const float inf = 1e8f;
            for (int i = 0; i < n; i++)
                _distMm[i] = _bits[i] ? inf : 0f;

            RelaxDistance(false);
            RelaxDistance(true);

            for (int i = 0; i < n; i++)
            {
                if (_distMm[i] >= inf * 0.5f)
                    _distMm[i] = 0f;
            }
        }

        private void RelaxDistance(bool backward)
        {
            int z0 = backward ? Nz - 1 : 0;
            int z1 = backward ? -1 : Nz;
            int zStep = backward ? -1 : 1;
            int y0 = backward ? Ny - 1 : 0;
            int y1 = backward ? -1 : Ny;
            int yStep = backward ? -1 : 1;
            int x0 = backward ? Nx - 1 : 0;
            int x1 = backward ? -1 : Nx;
            int xStep = backward ? -1 : 1;

            for (int iz = z0; iz != z1; iz += zStep)
            {
                for (int iy = y0; iy != y1; iy += yStep)
                {
                    for (int ix = x0; ix != x1; ix += xStep)
                    {
                        int index = LinearIndex(ix, iy, iz);
                        if (!_bits[index])
                            continue;

                        float best = _distMm[index];
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int jz = iz + dz;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int jy = iy + dy;
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0 && dz == 0)
                                        continue;
                                    int jx = ix + dx;
                                    double offset = Math.Sqrt(
                                        (dx * Dx) * (dx * Dx) +
                                        (dy * Dy) * (dy * Dy) +
                                        (dz * Dz) * (dz * Dz));
                                    float neighbor;
                                    if (jx < 0 || jy < 0 || jz < 0 || jx >= Nx || jy >= Ny || jz >= Nz)
                                        neighbor = 0f;
                                    else
                                        neighbor = _distMm[LinearIndex(jx, jy, jz)];
                                    float candidate = neighbor + (float)offset;
                                    if (candidate < best)
                                        best = candidate;
                                }
                            }
                        }
                        _distMm[index] = best;
                    }
                }
            }
        }

        private int LinearIndex(int ix, int iy, int iz)
        {
            return ix + Nx * (iy + Ny * iz);
        }
    }
}

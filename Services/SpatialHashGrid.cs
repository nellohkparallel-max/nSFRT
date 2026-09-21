using System;
using System.Collections.Generic;
using SFRThelper.Models;

namespace SFRThelper.Services
{
    /// <summary>
    /// Uniform 3D spatial hash for O(N) sphere-to-sphere clearance queries.
    /// Cell size equals the minimum allowed center distance.
    /// </summary>
    public sealed class SpatialHashGrid
    {
        private readonly double _cellSize;
        private readonly Dictionary<long, List<Point3D>> _cells;
        private readonly List<Point3D> _points;
        private const double Epsilon = 1e-6;

        public SpatialHashGrid(double cellSizeMm)
        {
            if (cellSizeMm <= 0)
                throw new ArgumentOutOfRangeException("cellSizeMm");
            _cellSize = cellSizeMm;
            _cells = new Dictionary<long, List<Point3D>>();
            _points = new List<Point3D>();
        }

        public int Count { get { return _points.Count; } }

        public void Add(Point3D point)
        {
            _points.Add(point);
            long key = Hash(point);
            List<Point3D> list;
            if (!_cells.TryGetValue(key, out list))
            {
                list = new List<Point3D>();
                _cells[key] = list;
            }
            list.Add(point);
        }

        public bool HasNeighborWithin(Point3D point, double minDistanceMm)
        {
            return TryFindNeighbor(point, minDistanceMm, null, out _);
        }

        public bool HasNeighborWithin(Point3D point, double minDistanceMm, Point3D? exclude)
        {
            return TryFindNeighbor(point, minDistanceMm, exclude, out _);
        }

        public bool TryFindNeighbor(Point3D point, double minDistanceMm, Point3D? exclude, out Point3D neighbor)
        {
            neighbor = default(Point3D);
            // 0.05 mm clinical/FP slop so exact lattice neighbors at distance d are retained.
            double thresh = Math.Max(0, minDistanceMm - 0.05);
            double threshSq = thresh * thresh;
            int ix = CellIndex(point.X);
            int iy = CellIndex(point.Y);
            int iz = CellIndex(point.Z);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        long key = Pack(ix + dx, iy + dy, iz + dz);
                        List<Point3D> list;
                        if (!_cells.TryGetValue(key, out list))
                            continue;

                        for (int i = 0; i < list.Count; i++)
                        {
                            Point3D candidate = list[i];
                            if (exclude.HasValue && candidate.DistanceSquaredTo(exclude.Value) < Epsilon)
                                continue;
                            double d2 = candidate.DistanceSquaredTo(point);
                            if (d2 < threshSq)
                            {
                                neighbor = candidate;
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        public bool WouldCollide(Point3D point, double minDistanceMm, Point3D? exclude)
        {
            return HasNeighborWithin(point, minDistanceMm, exclude);
        }

        public IReadOnlyList<Point3D> Points
        {
            get { return _points; }
        }

        private int CellIndex(double coordinate)
        {
            return (int)Math.Floor(coordinate / _cellSize);
        }

        private long Hash(Point3D point)
        {
            return Pack(CellIndex(point.X), CellIndex(point.Y), CellIndex(point.Z));
        }

        private static long Pack(int ix, int iy, int iz)
        {
            // 21 bits per axis, biased so negative indices pack safely.
            const int bias = 1 << 20;
            long x = (uint)(ix + bias) & 0x1FFFFF;
            long y = (uint)(iy + bias) & 0x1FFFFF;
            long z = (uint)(iz + bias) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }
    }
}

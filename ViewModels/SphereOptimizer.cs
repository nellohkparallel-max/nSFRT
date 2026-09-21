using System;
using System.Collections.Generic;
using SFRThelper.Models;
using SFRThelper.Services;

namespace SFRThelper.ViewModels
{
    /// <summary>
    /// CPU-only lattice generator. Phase 2 of the pipeline: zero VMS.TPS references.
    /// Anchors (0,0,0) at the target center of mass and rejects centers outside V_valid.
    /// </summary>
    public class SphereOptimizer
    {
        public const double DefaultCollisionEpsilonMm = 1e-4;

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters)
        {
            return GenerateLattice(geometry, parameters, null, System.Threading.CancellationToken.None);
        }

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            IReadOnlyList<SphereModel> fixedSpheres,
            System.Threading.CancellationToken token)
        {
            var result = new LatticePackingResult
            {
                PackingMode = parameters.PackingMode,
                AnchorCom = geometry != null ? geometry.CenterOfMass : default(Point3D)
            };

            if (geometry == null || parameters == null)
            {
                result.Message = "Geometry or parameters were not provided.";
                return result;
            }

            if (!geometry.IsValid)
            {
                result.Message = "V_valid is empty. No sphere centers can be placed.";
                return result;
            }

            double radius = parameters.SphereRadiusMm;
            double spacing = Math.Max(parameters.CenterSpacingMm, 2.0 * radius);
            var hash = new SpatialHashGrid(spacing);
            var occupied = new List<SphereModel>();

            if (fixedSpheres != null)
            {
                foreach (var fixedSphere in fixedSpheres)
                {
                    token.ThrowIfCancellationRequested();
                    if (fixedSphere == null)
                        continue;
                    hash.Add(fixedSphere.Center);
                    occupied.Add(fixedSphere);
                }
            }

            IList<Point3D> candidates = BuildCandidateCenters(geometry, parameters, spacing);
            result.CandidateCount = candidates.Count;

            int index = occupied.Count + 1;
            int maxCount = Math.Max(1, parameters.MaxSphereCount);

            for (int i = 0; i < candidates.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (occupied.Count >= maxCount)
                    break;

                Point3D center = candidates[i];
                if (!IsStrictlyInsideValidVolume(center, geometry.ValidVolume))
                {
                    result.RejectedOutsideValid++;
                    continue;
                }

                if (hash.HasNeighborWithin(center, spacing))
                {
                    result.RejectedCollision++;
                    continue;
                }

                hash.Add(center);
                occupied.Add(new SphereModel(center, radius, index));
                index++;
            }

            result.Spheres = occupied;
            result.PackingEfficiency = CalculatePackingEfficiency(occupied, radius, geometry.TargetBounds);
            result.Message = occupied.Count == 0
                ? "No valid sphere placements found inside V_valid."
                : "Packed " + occupied.Count + " spheres using COM-anchored "
                  + (parameters.PackingMode == PackingGeometryMode.HexagonalClosePacking ? "HCP/FCC" : "simple cubic")
                  + " lattice.";
            return result;
        }

        public bool IsValidEditedSpherePosition(
            Point3D candidateCenter,
            SphereModel movingSphere,
            IReadOnlyList<SphereModel> allSpheres,
            VoxelMask validVolume,
            double centerSpacingMm)
        {
            if (movingSphere == null)
                return false;
            if (!IsStrictlyInsideValidVolume(candidateCenter, validVolume))
                return false;

            double minDistance = Math.Max(centerSpacingMm, 2.0 * movingSphere.Radius);
            var hash = new SpatialHashGrid(minDistance);
            for (int i = 0; i < allSpheres.Count; i++)
            {
                SphereModel sphere = allSpheres[i];
                if (sphere == null || ReferenceEquals(sphere, movingSphere))
                    continue;
                hash.Add(sphere.Center);
            }

            return !hash.HasNeighborWithin(candidateCenter, minDistance);
        }

        public static bool IsStrictlyInsideValidVolume(Point3D center, VoxelMask validVolume)
        {
            if (validVolume == null || validVolume.IsEmpty)
                return false;
            return validVolume.Contains(center);
        }

        public IList<Point3D> BuildCandidateCenters(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            double spacing)
        {
            var centers = new List<Point3D>();
            LatticeTransform transform = geometry.Transform;
            if (Math.Abs(transform.Rxx) + Math.Abs(transform.Ryy) + Math.Abs(transform.Rzz) < 1e-12)
                transform = LatticeTransform.Identity(geometry.CenterOfMass);

            BoundingBox3D bounds = geometry.ValidVolume != null ? geometry.ValidVolume.Bounds : geometry.TargetBounds;
            if (bounds.IsEmpty)
                bounds = geometry.TargetBounds;

            double maxExtent = Math.Max(bounds.Diagonal, 1.0) + 2.0 * spacing;
            int n = (int)Math.Ceiling(maxExtent / Math.Max(spacing * 0.5, 1.0)) + 2;
            n = Math.Min(n, 80);

            if (parameters.PackingMode == PackingGeometryMode.SimpleCubic)
                AppendSimpleCubic(centers, transform, bounds, spacing, n);
            else
                AppendHexagonalClosePacking(centers, transform, bounds, spacing, n);

            return centers;
        }

        /// <summary>
        /// Simple cubic lattice: (i, j, k) * d, origin at COM.
        /// </summary>
        public static void AppendSimpleCubic(
            IList<Point3D> centers, LatticeTransform transform, BoundingBox3D bounds, double d, int n)
        {
            for (int k = -n; k <= n; k++)
            {
                for (int j = -n; j <= n; j++)
                {
                    for (int i = -n; i <= n; i++)
                    {
                        Point3D p = transform.ToPatient(i * d, j * d, k * d);
                        if (bounds.Contains(p))
                            centers.Add(p);
                    }
                }
            }
        }

        /// <summary>
        /// HCP packing with uniform nearest-neighbor spacing d.
        /// In-plane hexagonal rows plus ABAB layer offset so that interlayer neighbors are also distance d:
        /// x = i*d + (j mod 2)*d/2 + (k mod 2)*d/2
        /// y = j*(√3/2)*d + (k mod 2)*d*√3/6
        /// z = k*√(2/3)*d
        /// </summary>
        public static void AppendHexagonalClosePacking(
            IList<Point3D> centers, LatticeTransform transform, BoundingBox3D bounds, double d, int n)
        {
            double yPitch = d * Math.Sqrt(3.0) / 2.0;
            double zPitch = d * Math.Sqrt(2.0 / 3.0);
            double half = d * 0.5;
            double layerY = d * Math.Sqrt(3.0) / 6.0;

            for (int k = -n; k <= n; k++)
            {
                int kParity = k & 1;
                for (int j = -n; j <= n; j++)
                {
                    int jParity = j & 1;
                    double xOffset = (jParity + kParity) * half;
                    double y = j * yPitch + kParity * layerY;
                    double z = k * zPitch;
                    for (int i = -n; i <= n; i++)
                    {
                        Point3D p = transform.ToPatient(i * d + xOffset, y, z);
                        if (bounds.Contains(p))
                            centers.Add(p);
                    }
                }
            }
        }

        public static Point3D HexagonalLatticePoint(int i, int j, int k, double d)
        {
            double half = d * 0.5;
            int jParity = j & 1;
            int kParity = k & 1;
            double x = i * d + (jParity + kParity) * half;
            double y = j * (Math.Sqrt(3.0) / 2.0) * d + kParity * d * Math.Sqrt(3.0) / 6.0;
            double z = k * Math.Sqrt(2.0 / 3.0) * d;
            return new Point3D(x, y, z);
        }

        public static Point3D SimpleCubicLatticePoint(int i, int j, int k, double d)
        {
            return new Point3D(i * d, j * d, k * d);
        }

        private static double CalculatePackingEfficiency(IList<SphereModel> spheres, double radius, BoundingBox3D bounds)
        {
            if (spheres == null || spheres.Count == 0)
                return 0;
            double sphereVolume = (4.0 / 3.0) * Math.PI * radius * radius * radius;
            double boundsVolume = bounds.SizeX * bounds.SizeY * bounds.SizeZ;
            if (boundsVolume <= 0)
                return 0;
            return spheres.Count * sphereVolume / boundsVolume;
        }
    }
}

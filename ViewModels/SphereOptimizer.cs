using System;
using System.Collections.Generic;
using System.Threading;
using SFRThelper.Models;
using SFRThelper.Services;

namespace SFRThelper.ViewModels
{
    /// <summary>
    /// CPU-only lattice generator. Phase 2: zero VMS.TPS references.
    /// Anchors (0,0,0) at the target COM, applies planar yaw, and supports cubic / FCC / HCP.
    /// </summary>
    public class SphereOptimizer
    {
        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters)
        {
            return GenerateLattice(geometry, parameters, null, CancellationToken.None, null);
        }

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            IReadOnlyList<SphereModel> fixedSpheres,
            CancellationToken token)
        {
            return GenerateLattice(geometry, parameters, fixedSpheres, token, null);
        }

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            IReadOnlyList<SphereModel> fixedSpheres,
            CancellationToken token,
            IProgress<double> progress)
        {
            var result = new LatticePackingResult
            {
                PackingMode = parameters != null ? parameters.PackingMode : PackingGeometryMode.SimpleCubic,
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
            double dxy = Math.Max(parameters.EffectiveLateralSpacingMm, 2.0 * radius);
            double dz = Math.Max(parameters.EffectiveSiSpacingMm, 2.0 * radius);
            double minSpacing = Math.Min(dxy, dz);
            var hash = new SpatialHashGrid(minSpacing);
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

            IList<Point3D> candidates = BuildCandidateCenters(geometry, parameters, dxy, dz);
            result.CandidateCount = candidates.Count;

            int index = occupied.Count + 1;
            int maxCount = Math.Max(1, parameters.MaxSphereCount);

            for (int i = 0; i < candidates.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (progress != null && i % 64 == 0)
                    progress.Report(candidates.Count == 0 ? 1 : (double)i / candidates.Count);
                if (occupied.Count >= maxCount)
                    break;

                Point3D center = candidates[i];
                if (!IsStrictlyInsideValidVolume(center, geometry.ValidVolume))
                {
                    result.RejectedOutsideValid++;
                    continue;
                }

                if (hash.HasNeighborWithin(center, minSpacing))
                {
                    result.RejectedCollision++;
                    continue;
                }

                hash.Add(center);
                occupied.Add(new SphereModel(center, radius, index));
                index++;
            }

            if (progress != null)
                progress.Report(1.0);

            result.Spheres = occupied;
            result.PackingEfficiency = CalculatePackingEfficiency(occupied, radius, geometry.TargetBounds);
            result.Message = occupied.Count == 0
                ? "No valid sphere placements found inside V_valid."
                : "Packed " + occupied.Count + " spheres using COM-anchored "
                  + PackingLabel(parameters.PackingMode)
                  + " lattice (" + parameters.GridRotationDeg.ToString("F0") + "° yaw).";
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
            double dxy,
            double dz)
        {
            var centers = new List<Point3D>();
            LatticeTransform transform = geometry.Transform;
            if (Math.Abs(transform.Rxx) + Math.Abs(transform.Ryy) + Math.Abs(transform.Rzz) < 1e-12)
                transform = LatticeTransform.FromYawDegrees(geometry.CenterOfMass, parameters.GridRotationDeg);

            BoundingBox3D bounds = geometry.ValidVolume != null ? geometry.ValidVolume.Bounds : geometry.TargetBounds;
            if (bounds.IsEmpty)
                bounds = geometry.TargetBounds;

            double minPitch = Math.Max(Math.Min(dxy, dz) * 0.5, 1.0);
            double maxExtent = Math.Max(bounds.Diagonal, 1.0) + 2.0 * Math.Max(dxy, dz);
            int n = (int)Math.Ceiling(maxExtent / minPitch) + 2;
            n = Math.Min(n, 80);

            if (parameters.PackingMode == PackingGeometryMode.SimpleCubic)
                AppendSimpleCubic(centers, transform, bounds, dxy, dz, n);
            else if (parameters.PackingMode == PackingGeometryMode.FaceCenteredCubic)
                AppendFaceCenteredCubic(centers, transform, bounds, dxy, dz, n);
            else
                AppendHexagonalClosePacking(centers, transform, bounds, dxy, dz, n);

            return centers;
        }

        public static void AppendSimpleCubic(
            IList<Point3D> centers, LatticeTransform transform, BoundingBox3D bounds, double dxy, double dz, int n)
        {
            for (int k = -n; k <= n; k++)
            {
                for (int j = -n; j <= n; j++)
                {
                    for (int i = -n; i <= n; i++)
                    {
                        Point3D p = transform.ToPatient(i * dxy, j * dxy, k * dz);
                        if (bounds.Contains(p))
                            centers.Add(p);
                    }
                }
            }
        }

        /// <summary>
        /// FCC: cubic lattice with even i+j+k on a grid of pitch d/√2 so nearest neighbors are distance d.
        /// </summary>
        public static void AppendFaceCenteredCubic(
            IList<Point3D> centers, LatticeTransform transform, BoundingBox3D bounds, double dxy, double dz, int n)
        {
            double sxy = dxy / Math.Sqrt(2.0);
            double sz = dz / Math.Sqrt(2.0);
            int n3 = n * 2;
            for (int k = -n3; k <= n3; k++)
            {
                for (int j = -n3; j <= n3; j++)
                {
                    for (int i = -n3; i <= n3; i++)
                    {
                        if (((i + j + k) & 1) != 0)
                            continue;
                        Point3D p = transform.ToPatient(i * sxy, j * sxy, k * sz);
                        if (bounds.Contains(p))
                            centers.Add(p);
                    }
                }
            }
        }

        public static void AppendHexagonalClosePacking(
            IList<Point3D> centers, LatticeTransform transform, BoundingBox3D bounds, double dxy, double dz, int n)
        {
            double yPitch = dxy * Math.Sqrt(3.0) / 2.0;
            double zPitch = dz * Math.Sqrt(2.0 / 3.0);
            double half = dxy * 0.5;
            double layerY = dxy * Math.Sqrt(3.0) / 6.0;

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
                        Point3D p = transform.ToPatient(i * dxy + xOffset, y, z);
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

        public static Point3D FaceCenteredCubicPoint(int i, int j, int k, double d)
        {
            double s = d / Math.Sqrt(2.0);
            return new Point3D(i * s, j * s, k * s);
        }

        public static string PackingLabel(PackingGeometryMode mode)
        {
            if (mode == PackingGeometryMode.FaceCenteredCubic)
                return "FCC";
            if (mode == PackingGeometryMode.HexagonalClosePacking)
                return "HCP";
            return "simple cubic";
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

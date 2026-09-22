using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using SFRThelper.Helpers;
using SFRThelper.Models;
using SFRThelper.Services;

namespace SFRThelper.ViewModels
{
    /// <summary>
    /// CPU-only lattice generator. Phase 2: zero VMS.TPS references.
    /// Anchors (0,0,0) at the target COM, applies planar yaw, and supports cubic / FCC / HCP.
    /// Optional maximization: rigid unit-cell phase-shift search or particle relaxation.
    /// </summary>
    public class SphereOptimizer
    {
        public const double ScoreDistanceWeight = 1e-4;

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters)
        {
            return GenerateLattice(geometry, parameters, null, CancellationToken.None, null, null);
        }

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            IReadOnlyList<SphereModel> fixedSpheres,
            CancellationToken token)
        {
            return GenerateLattice(geometry, parameters, fixedSpheres, token, null, null);
        }

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            IReadOnlyList<SphereModel> fixedSpheres,
            CancellationToken token,
            IProgress<double> progress)
        {
            return GenerateLattice(geometry, parameters, fixedSpheres, token, progress, null);
        }

        public LatticePackingResult GenerateLattice(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            IReadOnlyList<SphereModel> fixedSpheres,
            CancellationToken token,
            IProgress<double> progress,
            IProgress<string> status)
        {
            var result = new LatticePackingResult
            {
                PackingMode = parameters != null ? parameters.PackingMode : PackingGeometryMode.SimpleCubic,
                AnchorCom = geometry != null ? geometry.CenterOfMass : default(Point3D),
                Strategy = parameters != null ? parameters.OptimizationStrategy : SphereMaximizationStrategy.RigidPhaseShift
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

            if (geometry.ValidVolume != null)
                geometry.ValidVolume.ComputeDistanceField();

            double radius = parameters.SphereRadiusMm;
            double dxy = Math.Max(parameters.EffectiveLateralSpacingMm, 2.0 * radius);
            double dz = Math.Max(parameters.EffectiveSiSpacingMm, 2.0 * radius);
            double minSpacing = Math.Min(dxy, dz);
            int maxCount = Math.Max(1, parameters.MaxSphereCount);

            LatticeTransform transform = geometry.Transform;
            if (Math.Abs(transform.Rxx) + Math.Abs(transform.Ryy) + Math.Abs(transform.Rzz) < 1e-12)
                transform = LatticeTransform.FromYawDegrees(geometry.CenterOfMass, parameters.GridRotationDeg);

            List<Point3D> local = BuildLocalLatticePoints(geometry, parameters, dxy, dz);

            PackingTrial baseline = PackShift(
                geometry, local, transform, new Point3D(0, 0, 0),
                radius, minSpacing, maxCount, fixedSpheres, token);
            result.BaselineCount = baseline.Count;
            result.CandidateCount = local.Count;
            result.RejectedOutsideValid = baseline.RejectedOutside;
            result.RejectedCollision = baseline.RejectedCollision;

            PackingTrial best = baseline;
            Point3D bestShift = new Point3D(0, 0, 0);

            Report(status, FormatLive(baseline.Count, baseline.Count, bestShift, 0, 0));

            if (parameters.EnableSphereMaximization && (fixedSpheres == null || fixedSpheres.Count == 0))
            {
                result.MaximizationRan = true;
                int iterations = Math.Max(10, Math.Min(1000, parameters.MaxIterations));
                if (parameters.OptimizationStrategy == SphereMaximizationStrategy.ParticleRelaxation)
                {
                    PackingTrial relaxed = RelaxParticles(
                        geometry, baseline, radius, minSpacing, maxCount, iterations, token, progress, status);
                    if (CompareTrials(relaxed, best) > 0)
                    {
                        best = relaxed;
                        bestShift = new Point3D(0, 0, 0);
                    }
                }
                else
                {
                    int samples = BuildPhaseShiftSamples(dxy, dz, iterations, out List<Point3D> shifts);
                    for (int i = 0; i < samples; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (progress != null)
                            progress.Report((i + 1) / (double)samples);
                        Point3D shift = shifts[i];
                        PackingTrial trial = PackShift(
                            geometry, local, transform, shift, radius, minSpacing, maxCount, null, token);
                        if (CompareTrials(trial, best) > 0)
                        {
                            best = trial;
                            bestShift = shift;
                        }
                        if (i == 0 || (i + 1) % 5 == 0 || i + 1 == samples)
                        {
                            Report(status, FormatLive(baseline.Count, best.Count, bestShift, i + 1, samples));
                        }
                    }
                }
            }
            else if (progress != null)
            {
                progress.Report(1.0);
            }

            if (progress != null)
                progress.Report(1.0);

            var occupied = new List<SphereModel>();
            int index = 1;
            foreach (var center in best.Centers)
            {
                occupied.Add(new SphereModel(center, radius, index));
                index++;
            }

            result.Spheres = occupied;
            result.OptimizedCount = occupied.Count;
            result.SphereCountGain = result.OptimizedCount - result.BaselineCount;
            result.ShiftXMm = bestShift.X;
            result.ShiftYMm = bestShift.Y;
            result.ShiftZMm = bestShift.Z;
            result.MeanBoundaryDistanceMm = best.MeanBoundaryDistance;
            result.Score = Score(best.Count, best.MeanBoundaryDistance);
            result.PackingEfficiency = CalculatePackingEfficiency(occupied, radius, geometry.TargetBounds);
            result.Message = BuildMessage(result, parameters);
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

        public static double Score(int sphereCount, double meanBoundaryDistanceMm)
        {
            if (sphereCount <= 0)
                return 0;
            return sphereCount + ScoreDistanceWeight * meanBoundaryDistanceMm;
        }

        public static double Halton(int index, int numberBase)
        {
            if (index < 0)
                index = 0;
            if (numberBase < 2)
                numberBase = 2;
            double f = 1.0;
            double r = 0.0;
            int i = index + 1;
            while (i > 0)
            {
                f /= numberBase;
                r += f * (i % numberBase);
                i /= numberBase;
            }
            return r;
        }

        public IList<Point3D> BuildCandidateCenters(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            double dxy,
            double dz)
        {
            return BuildCandidateCenters(geometry, parameters, dxy, dz, new Point3D(0, 0, 0));
        }

        public IList<Point3D> BuildCandidateCenters(
            LatticeGeometryContext geometry,
            SFRTParameters parameters,
            double dxy,
            double dz,
            Point3D localShift)
        {
            var centers = new List<Point3D>();
            LatticeTransform transform = geometry.Transform;
            if (Math.Abs(transform.Rxx) + Math.Abs(transform.Ryy) + Math.Abs(transform.Rzz) < 1e-12)
                transform = LatticeTransform.FromYawDegrees(geometry.CenterOfMass, parameters.GridRotationDeg);
            transform = transform.WithLocalShift(localShift.X, localShift.Y, localShift.Z);

            BoundingBox3D bounds = geometry.ValidVolume != null ? geometry.ValidVolume.Bounds : geometry.TargetBounds;
            if (bounds.IsEmpty)
                bounds = geometry.TargetBounds;

            List<Point3D> local = BuildLocalLatticePoints(geometry, parameters, dxy, dz);
            for (int i = 0; i < local.Count; i++)
            {
                Point3D p = transform.ToPatient(local[i].X, local[i].Y, local[i].Z);
                if (bounds.Contains(p))
                    centers.Add(p);
            }
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

        private List<Point3D> BuildLocalLatticePoints(
            LatticeGeometryContext geometry, SFRTParameters parameters, double dxy, double dz)
        {
            BoundingBox3D bounds = geometry.ValidVolume != null ? geometry.ValidVolume.Bounds : geometry.TargetBounds;
            if (bounds.IsEmpty)
                bounds = geometry.TargetBounds;

            double minPitch = Math.Max(Math.Min(dxy, dz) * 0.5, 1.0);
            double maxExtent = Math.Max(bounds.Diagonal, 1.0) + 2.0 * Math.Max(dxy, dz);
            int n = (int)Math.Ceiling(maxExtent / minPitch) + 2;
            n = Math.Min(n, 36);

            var identity = LatticeTransform.Identity(new Point3D(0, 0, 0));
            var infinite = BoundingBox3D.FromMinMax(-1e6, -1e6, -1e6, 1e6, 1e6, 1e6);
            var local = new List<Point3D>();
            if (parameters.PackingMode == PackingGeometryMode.SimpleCubic)
                AppendSimpleCubic(local, identity, infinite, dxy, dz, n);
            else if (parameters.PackingMode == PackingGeometryMode.FaceCenteredCubic)
                AppendFaceCenteredCubic(local, identity, infinite, dxy, dz, n);
            else
                AppendHexagonalClosePacking(local, identity, infinite, dxy, dz, n);

            // Keep only locals that could land near the valid AABB for some unit-cell shift.
            double pad = Math.Max(dxy, dz) + 1.0;
            var padded = BoundingBox3D.FromMinMax(
                bounds.MinX - pad, bounds.MinY - pad, bounds.MinZ - pad,
                bounds.MaxX + pad, bounds.MaxY + pad, bounds.MaxZ + pad);
            LatticeTransform transform = geometry.Transform;
            if (Math.Abs(transform.Rxx) + Math.Abs(transform.Ryy) + Math.Abs(transform.Rzz) < 1e-12)
                transform = LatticeTransform.FromYawDegrees(geometry.CenterOfMass, parameters.GridRotationDeg);

            var filtered = new List<Point3D>(local.Count / 4 + 1);
            for (int i = 0; i < local.Count; i++)
            {
                Point3D p = transform.ToPatient(local[i].X, local[i].Y, local[i].Z);
                if (padded.Contains(p))
                    filtered.Add(local[i]);
            }
            return filtered.Count > 0 ? filtered : local;
        }

        private PackingTrial PackShift(
            LatticeGeometryContext geometry,
            IList<Point3D> localPoints,
            LatticeTransform transform,
            Point3D shift,
            double radius,
            double minSpacing,
            int maxCount,
            IReadOnlyList<SphereModel> fixedSpheres,
            CancellationToken token)
        {
            var trial = new PackingTrial();
            var hash = new SpatialHashGrid(minSpacing);
            LatticeTransform shifted = transform.WithLocalShift(shift.X, shift.Y, shift.Z);

            if (fixedSpheres != null)
            {
                for (int i = 0; i < fixedSpheres.Count; i++)
                {
                    if (fixedSpheres[i] == null)
                        continue;
                    hash.Add(fixedSpheres[i].Center);
                    trial.Centers.Add(fixedSpheres[i].Center);
                }
            }

            for (int i = 0; i < localPoints.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (trial.Centers.Count >= maxCount)
                    break;

                Point3D center = shifted.ToPatient(localPoints[i].X, localPoints[i].Y, localPoints[i].Z);
                if (!IsStrictlyInsideValidVolume(center, geometry.ValidVolume))
                {
                    trial.RejectedOutside++;
                    continue;
                }
                if (hash.HasNeighborWithin(center, minSpacing))
                {
                    trial.RejectedCollision++;
                    continue;
                }
                hash.Add(center);
                trial.Centers.Add(center);
            }

            trial.MeanBoundaryDistance = MeanBoundaryDistance(trial.Centers, geometry.ValidVolume);
            return trial;
        }

        private PackingTrial RelaxParticles(
            LatticeGeometryContext geometry,
            PackingTrial baseline,
            double radius,
            double minSpacing,
            int maxCount,
            int iterations,
            CancellationToken token,
            IProgress<double> progress,
            IProgress<string> status)
        {
            var particles = new List<Point3D>(baseline.Centers);
            VoxelMask mask = geometry.ValidVolume;
            SeedExtraParticles(mask, minSpacing, particles);

            double step = Math.Min(minSpacing * 0.2, 1.5);
            for (int iter = 0; iter < iterations; iter++)
            {
                token.ThrowIfCancellationRequested();
                if (progress != null)
                    progress.Report((iter + 1) / (double)iterations);

                var hash = new SpatialHashGrid(minSpacing);
                for (int i = 0; i < particles.Count; i++)
                    hash.Add(particles[i]);

                var next = new List<Point3D>(particles.Count);
                for (int i = 0; i < particles.Count; i++)
                {
                    Point3D p = particles[i];
                    Point3D force = new Point3D(0, 0, 0);
                    IReadOnlyList<Point3D> all = hash.Points;
                    for (int j = 0; j < all.Count; j++)
                    {
                        double d2 = p.DistanceSquaredTo(all[j]);
                        if (d2 < 1e-8 || d2 > minSpacing * minSpacing * 1.44)
                            continue;
                        double d = Math.Sqrt(d2);
                        Point3D dir = p.Subtract(all[j]).NormalizedOrZero();
                        double mag = (minSpacing - d) / minSpacing;
                        force = force.Add(dir.Scale(mag));
                    }

                    double dist = mask.DistanceToBoundaryMm(p);
                    if (dist < minSpacing * 0.35)
                        force = force.Add(mask.DistanceGradient(p).Scale(0.6));

                    Point3D candidate = p.Add(force.Scale(step));
                    if (IsStrictlyInsideValidVolume(candidate, mask))
                        next.Add(candidate);
                    else
                        next.Add(p);
                }
                particles = next;

                if (iter % 8 == 0)
                    TryInsertParticles(mask, minSpacing, particles, maxCount * 3);

                if (iter == 0 || (iter + 1) % 5 == 0 || iter + 1 == iterations)
                {
                    PackingTrial snapshot = GreedySelect(particles, mask, minSpacing, maxCount);
                    Report(status, string.Format(CultureInfo.InvariantCulture,
                        "Baseline Count: {0} → relaxation {1}/{2}: {3} spheres",
                        baseline.Count, iter + 1, iterations, snapshot.Count));
                }
            }

            PackingTrial selected = GreedySelect(particles, mask, minSpacing, maxCount);
            if (CompareTrials(selected, baseline) < 0)
                return baseline;
            return selected;
        }

        private static void SeedExtraParticles(VoxelMask mask, double minSpacing, List<Point3D> particles)
        {
            if (mask == null || mask.IsEmpty)
                return;
            int skip = Math.Max(1, (int)Math.Round(minSpacing * 0.45 / Math.Max(mask.Dx, 0.5)));
            var hash = new SpatialHashGrid(minSpacing);
            for (int i = 0; i < particles.Count; i++)
                hash.Add(particles[i]);

            int added = 0;
            for (int iz = 0; iz < mask.Nz && added < 400; iz += skip)
            {
                for (int iy = 0; iy < mask.Ny && added < 400; iy += skip)
                {
                    for (int ix = 0; ix < mask.Nx && added < 400; ix += skip)
                    {
                        if (!mask.Get(ix, iy, iz))
                            continue;
                        Point3D p = mask.VoxelCenter(ix, iy, iz);
                        if (mask.DistanceToBoundaryMm(p) < 0.4)
                            continue;
                        if (hash.HasNeighborWithin(p, minSpacing * 0.85))
                            continue;
                        hash.Add(p);
                        particles.Add(p);
                        added++;
                    }
                }
            }
        }

        private static void TryInsertParticles(VoxelMask mask, double minSpacing, List<Point3D> particles, int cap)
        {
            if (particles.Count >= cap || mask == null)
                return;
            var hash = new SpatialHashGrid(minSpacing);
            for (int i = 0; i < particles.Count; i++)
                hash.Add(particles[i]);

            int skip = Math.Max(1, (int)Math.Round(minSpacing * 0.5 / Math.Max(mask.Dx, 0.5)));
            for (int iz = skip / 2; iz < mask.Nz && particles.Count < cap; iz += skip)
            {
                for (int iy = skip / 2; iy < mask.Ny && particles.Count < cap; iy += skip)
                {
                    for (int ix = skip / 2; ix < mask.Nx && particles.Count < cap; ix += skip)
                    {
                        if (!mask.Get(ix, iy, iz))
                            continue;
                        Point3D p = mask.VoxelCenter(ix, iy, iz);
                        if (hash.HasNeighborWithin(p, minSpacing))
                            continue;
                        hash.Add(p);
                        particles.Add(p);
                    }
                }
            }
        }

        private static PackingTrial GreedySelect(List<Point3D> particles, VoxelMask mask, double minSpacing, int maxCount)
        {
            var ranked = new List<Point3D>(particles);
            ranked.Sort((a, b) => mask.DistanceToBoundaryMm(b).CompareTo(mask.DistanceToBoundaryMm(a)));
            var trial = new PackingTrial();
            var hash = new SpatialHashGrid(minSpacing);
            for (int i = 0; i < ranked.Count && trial.Centers.Count < maxCount; i++)
            {
                Point3D p = ranked[i];
                if (!IsStrictlyInsideValidVolume(p, mask))
                    continue;
                if (hash.HasNeighborWithin(p, minSpacing))
                    continue;
                hash.Add(p);
                trial.Centers.Add(p);
            }
            trial.MeanBoundaryDistance = MeanBoundaryDistance(trial.Centers, mask);
            return trial;
        }

        private static int BuildPhaseShiftSamples(double dxy, double dz, int maxIterations, out List<Point3D> shifts)
        {
            shifts = new List<Point3D>(maxIterations + 8);
            shifts.Add(new Point3D(dxy * 0.5, 0, 0));
            shifts.Add(new Point3D(0, dxy * 0.5, 0));
            shifts.Add(new Point3D(0, 0, dz * 0.5));
            shifts.Add(new Point3D(dxy * 0.5, dxy * 0.5, dz * 0.5));
                shifts.Add(new Point3D(dxy * 0.25, dxy * 0.75, dz * 0.25));
            int i = 0;
            while (shifts.Count < maxIterations)
            {
                double sx, sy, sz;
                SobolSequence.UnitCube(i, out sx, out sy, out sz);
                shifts.Add(new Point3D(sx * dxy, sy * dxy, sz * dz));
                i++;
            }
            if (shifts.Count > maxIterations)
                shifts.RemoveRange(maxIterations, shifts.Count - maxIterations);
            return shifts.Count;
        }

        private static int CompareTrials(PackingTrial a, PackingTrial b)
        {
            double sa = Score(a.Count, a.MeanBoundaryDistance);
            double sb = Score(b.Count, b.MeanBoundaryDistance);
            if (sa > sb + 1e-12)
                return 1;
            if (sa < sb - 1e-12)
                return -1;
            return 0;
        }

        private static double MeanBoundaryDistance(IList<Point3D> centers, VoxelMask mask)
        {
            if (centers == null || centers.Count == 0 || mask == null)
                return 0;
            double sum = 0;
            for (int i = 0; i < centers.Count; i++)
                sum += mask.DistanceToBoundaryMm(centers[i]);
            return sum / centers.Count;
        }

        private static string FormatLive(int baseline, int best, Point3D shift, int trial, int total)
        {
            if (total <= 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Baseline Count: {0}", baseline);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "Baseline Count: {0} → Optimized Count: {1} ({2:+0;-#;+0} spheres, Shift: Δx={3:+0.0;-0.0;+0.0}, Δy={4:+0.0;-0.0;+0.0}, Δz={5:+0.0;-0.0;+0.0} mm)  [{6}/{7}]",
                baseline, best, best - baseline, shift.X, shift.Y, shift.Z, trial, total);
        }

        private static string BuildMessage(LatticePackingResult result, SFRTParameters parameters)
        {
            if (result.OptimizedCount == 0)
                return "No valid sphere placements found inside V_valid.";

            string packing = "Packed " + result.OptimizedCount + " spheres using COM-anchored "
                + PackingLabel(parameters.PackingMode)
                + " lattice (" + parameters.GridRotationDeg.ToString("F0", CultureInfo.InvariantCulture) + "° yaw).";

            if (!result.MaximizationRan)
                return packing;

            return string.Format(CultureInfo.InvariantCulture,
                "Baseline Count: {0} → Optimized Count: {1} ({2:+0;-#;+0} spheres, Shift: Δx={3:+0.0;-0.0;+0.0}, Δy={4:+0.0;-0.0;+0.0}, Δz={5:+0.0;-0.0;+0.0} mm)",
                result.BaselineCount, result.OptimizedCount, result.SphereCountGain,
                result.ShiftXMm, result.ShiftYMm, result.ShiftZMm);
        }

        private static void Report(IProgress<string> status, string message)
        {
            if (status != null)
                status.Report(message);
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

        private sealed class PackingTrial
        {
            public List<Point3D> Centers = new List<Point3D>();
            public int RejectedOutside;
            public int RejectedCollision;
            public double MeanBoundaryDistance;
            public int Count { get { return Centers.Count; } }
        }
    }
}

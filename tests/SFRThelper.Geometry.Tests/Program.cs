using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SFRThelper.Helpers;
using SFRThelper.Models;
using SFRThelper.Services;
using SFRThelper.ViewModels;

namespace SFRThelper.Geometry.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("HCP nearest-neighbor spacing equals d", TestHcpNearestNeighbor);
            Run("Simple cubic nearest-neighbor spacing equals d", TestCubicNearestNeighbor);
            Run("COM-anchored origin is lattice (0,0,0)", TestComAnchor);
            Run("VoxelMask contains only occupied voxels", TestVoxelMask);
            Run("Spatial hash rejects sub-minimum distances in O(N)", TestSpatialHash);
            Run("Lattice generation stays inside V_valid", TestValidVolumeFilter);
            Run("Spacing validation requires d >= 2r", TestParameterValidation);
            Run("TG-263 Peak ids and 16-character limit", TestStructureNaming);
            Run("Transaction rollback is sequential reverse order", TestTransactionRollback);
            Run("PVDR and volume-fraction flags", TestMetrics);
            Run("Evaluation service assembles PVDR rows and alerts", TestEvaluationService);
            Run("Feasibility uses packed count and clearances", TestFeasibility);

            Console.WriteLine();
            Console.WriteLine("Passed: " + _passed.ToString(CultureInfo.InvariantCulture)
                + "   Failed: " + _failed.ToString(CultureInfo.InvariantCulture));
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name);
                Console.WriteLine("       " + ex.Message);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertNear(double actual, double expected, double tol, string label)
        {
            if (double.IsNaN(actual) || Math.Abs(actual - expected) > tol)
            {
                throw new Exception(string.Format(CultureInfo.InvariantCulture,
                    "{0}: expected {1}, actual {2}", label, expected, actual));
            }
        }

        private static void TestHcpNearestNeighbor()
        {
            const double d = 15.0;
            Point3D origin = SphereOptimizer.HexagonalLatticePoint(0, 0, 0, d);
            var neighbors = new[]
            {
                SphereOptimizer.HexagonalLatticePoint(1, 0, 0, d),
                SphereOptimizer.HexagonalLatticePoint(0, 1, 0, d),
                SphereOptimizer.HexagonalLatticePoint(0, 0, 1, d),
                SphereOptimizer.HexagonalLatticePoint(-1, 0, 0, d)
            };
            foreach (var n in neighbors)
            {
                double dist = origin.DistanceTo(n);
                AssertNear(dist, d, 1e-6, "HCP neighbor " + n);
            }
        }

        private static void TestCubicNearestNeighbor()
        {
            const double d = 12.0;
            Point3D origin = SphereOptimizer.SimpleCubicLatticePoint(0, 0, 0, d);
            AssertNear(origin.DistanceTo(SphereOptimizer.SimpleCubicLatticePoint(1, 0, 0, d)), d, 1e-9, "cubic x");
            AssertNear(origin.DistanceTo(SphereOptimizer.SimpleCubicLatticePoint(0, 1, 0, d)), d, 1e-9, "cubic y");
            AssertNear(origin.DistanceTo(SphereOptimizer.SimpleCubicLatticePoint(0, 0, 1, d)), d, 1e-9, "cubic z");
        }

        private static void TestComAnchor()
        {
            var com = new Point3D(10, -4, 27);
            var t = LatticeTransform.Identity(com);
            Point3D p = t.ToPatient(0, 0, 0);
            AssertNear(p.X, com.X, 1e-12, "COM X");
            AssertNear(p.Y, com.Y, 1e-12, "COM Y");
            AssertNear(p.Z, com.Z, 1e-12, "COM Z");
        }

        private static void TestVoxelMask()
        {
            var mask = new VoxelMask(0, 0, 0, 2, 2, 2, 5, 5, 5);
            mask.Set(1, 1, 1, true);
            AssertTrue(mask.Contains(new Point3D(3, 3, 3)), "center of occupied voxel should be inside");
            AssertTrue(!mask.Contains(new Point3D(7, 7, 7)), "empty voxel should be outside");
            AssertTrue(!mask.Contains(new Point3D(-1, 0, 0)), "out of bounds should be outside");
            AssertTrue(mask.OccupiedCount == 1, "occupied count");
        }

        private static void TestSpatialHash()
        {
            var hash = new SpatialHashGrid(10);
            hash.Add(new Point3D(0, 0, 0));
            hash.Add(new Point3D(50, 0, 0));
            AssertTrue(hash.HasNeighborWithin(new Point3D(4, 0, 0), 10), "close point collides");
            AssertTrue(!hash.HasNeighborWithin(new Point3D(25, 0, 0), 10), "midpoint is free");
            AssertTrue(hash.Count == 2, "count");
        }

        private static void TestValidVolumeFilter()
        {
            var mask = new VoxelMask(-20, -20, -20, 2, 2, 2, 20, 20, 20);
            for (int iz = 5; iz < 15; iz++)
                for (int iy = 5; iy < 15; iy++)
                    for (int ix = 5; ix < 15; ix++)
                        mask.Set(ix, iy, iz, true);

            var geometry = new LatticeGeometryContext
            {
                CenterOfMass = new Point3D(0, 0, 0),
                TargetBounds = BoundingBox3D.FromMinMax(-20, -20, -20, 20, 20, 20),
                ValidVolume = mask,
                TargetVolumeCc = 100,
                ValidVolumeCc = mask.VolumeCc,
                Transform = LatticeTransform.Identity(new Point3D(0, 0, 0))
            };
            var parameters = new SFRTParameters
            {
                SphereRadiusMm = 5,
                CenterSpacingMm = 15,
                PackingMode = PackingGeometryMode.HexagonalClosePacking,
                MaxSphereCount = 200,
                SelectedTargetId = "GTV"
            };

            var optimizer = new SphereOptimizer();
            LatticePackingResult result = optimizer.GenerateLattice(geometry, parameters);
            AssertTrue(result.SphereCount > 0, "expected packed spheres");
            foreach (var sphere in result.Spheres)
                AssertTrue(mask.Contains(sphere.Center), "sphere center outside V_valid: " + sphere.Center);

            var hash = new SpatialHashGrid(parameters.CenterSpacingMm);
            foreach (var sphere in result.Spheres)
            {
                AssertTrue(!hash.HasNeighborWithin(sphere.Center, parameters.CenterSpacingMm),
                    "packed spheres violate spacing at " + sphere.Center);
                hash.Add(sphere.Center);
            }
        }

        private static void TestParameterValidation()
        {
            var p = new SFRTParameters
            {
                SelectedTargetId = "GTV",
                SphereRadiusMm = 5,
                CenterSpacingMm = 9
            };
            AssertTrue(!p.IsValid, "spacing < 2r should be invalid");
            p.CenterSpacingMm = 10;
            AssertTrue(p.IsValid, "spacing == 2r should be valid");
            p.Oar1StructureId = "GTV";
            AssertTrue(!p.IsValid, "OAR cannot equal target");
            p.Oar1StructureId = "Cord";
            AssertTrue(p.IsValid, "distinct OAR should be valid");
        }

        private static void TestStructureNaming()
        {
            AssertTrue(StructureNaming.FormatPeakId(1) == "Peak_01", "Peak_01");
            AssertTrue(StructureNaming.FormatPeakId(12) == "Peak_12", "Peak_12");
            AssertTrue(StructureNaming.IsIndividualPeakId("Peak_03"), "regex Peak_03");
            AssertTrue(!StructureNaming.IsIndividualPeakId("Lattice_Peaks"), "composite is not individual");
            AssertTrue(StructureNaming.CompositePeaksId.Length <= StructureNaming.MaxIdLength, "Lattice_Peaks length");
            AssertTrue(StructureNaming.ValleyId.Length <= StructureNaming.MaxIdLength, "Lattice_Valley length");
            var existing = new HashSet<string> { "Lattice_Peaks" };
            string next = StructureNaming.NextAvailable("Lattice_Peaks", existing);
            AssertTrue(next != "Lattice_Peaks", "unique id");
            AssertTrue(next.Length <= 16, "unique id length");
        }

        private static void TestTransactionRollback()
        {
            var tx = new StructureTransaction();
            tx.Track("Peak_01");
            tx.Track("Peak_02");
            tx.Track("Lattice_Peaks");
            var order = tx.RollbackOrder().ToList();
            AssertTrue(order.Count == 3, "count");
            AssertTrue(order[0] == "Lattice_Peaks" && order[2] == "Peak_01", "reverse sequential rollback");
        }

        private static void TestMetrics()
        {
            AssertNear(SfrtMetricsCalculator.Pvdr(20, 5), 4.0, 1e-9, "PVDR");
            AssertTrue(SfrtMetricsCalculator.IsPvdrLow(2.4), "PVDR < 2.5");
            AssertTrue(!SfrtMetricsCalculator.IsPvdrLow(2.5), "PVDR == 2.5");
            AssertNear(SfrtMetricsCalculator.VolumeFractionPercent(3, 100), 3.0, 1e-9, "VF");
            AssertTrue(SfrtMetricsCalculator.IsVolumeFractionOutOfRange(0.5), "VF low");
            AssertTrue(SfrtMetricsCalculator.IsVolumeFractionOutOfRange(6.0), "VF high");
            AssertTrue(!SfrtMetricsCalculator.IsVolumeFractionOutOfRange(3.0), "VF ok");
        }

        private static void TestEvaluationService()
        {
            var esapi = new FakeEsapi();
            var svc = new SFRTEvaluationService(esapi);
            var parameters = new SFRTParameters { SelectedTargetId = "GTV" };
            SFRTEvaluationResult result = svc.Evaluate("PlanSetup|C1|SBRT", parameters);
            AssertTrue(result.Success, "evaluation success");
            AssertNear(result.PvdrMean, 4.0, 1e-9, "PVDR_mean");
            AssertNear(result.Pvdr10_90, 4.0, 1e-9, "PVDR_10/90");
            AssertNear(result.VolumeFractionPercent, 3.0, 1e-9, "volume fraction");
            AssertTrue(result.MetricRows.Count > 0, "metric rows");
            AssertTrue(result.IndividualPeaks.Count == 2, "individual peaks");
            AssertTrue(!SfrtMetricsCalculator.IsPvdrLow(result.PvdrMean), "healthy PVDR");
        }

        private static void TestFeasibility()
        {
            var geometry = new LatticeGeometryContext
            {
                TargetVolumeCc = 80,
                ValidVolumeCc = 40,
                ValidVolume = VoxelMask.Empty()
            };
            // Empty mask => IsValid false, but we still want clearance math.
            geometry.ValidVolume.Set(0, 0, 0, true);
            var p = new SFRTParameters
            {
                SphereRadiusMm = 5,
                CenterSpacingMm = 15,
                TargetClearanceMm = 5,
                Oar1ClearanceMm = 10,
                SelectedTargetId = "GTV"
            };
            FeasibilitySummary s = SfrtMetricsCalculator.BuildFeasibility(geometry, p, 8);
            AssertNear(s.EdgeToEdgeClearanceMm, 5.0, 1e-9, "edge-to-edge");
            AssertTrue(s.EstimatedSphereCount == 8, "count");
            AssertNear(s.VolumeFractionPercent, 8 * (4.0 / 3.0) * Math.PI * 125 / 1000.0 / 80.0 * 100.0, 1e-6, "fraction");
        }

        private sealed class FakeEsapi : IESAPIService
        {
            public bool CanModifyStructureSet(out string reason) { reason = null; return true; }
            public StructureCreationResult CreateLatticeStructures(IReadOnlyList<SphereModel> spheres, SFRTParameters parameters, IProgress<string> progress, CancellationToken token) { throw new NotImplementedException(); }
            public LatticeGeometryContext ExtractLatticeGeometry(SFRTParameters parameters, IProgress<string> progress, CancellationToken token) { throw new NotImplementedException(); }
            public IReadOnlyList<StructureListItem> GetAvoidanceStructures() { return new List<StructureListItem>(); }
            public IReadOnlyList<PlanListItem> GetEvaluablePlans() { return new List<PlanListItem>(); }
            public ImageGeometryDto GetImageGeometry() { return new ImageGeometryDto(); }
            public string GetPatientStatus() { return "Ready"; }
            public IReadOnlyList<IReadOnlyList<Point3D>> GetStructureContoursOnSlice(string structureId, int sliceIndex) { return new List<IReadOnlyList<Point3D>>(); }
            public StructureListItem GetStructureInfo(string structureId) { return null; }
            public IReadOnlyList<StructureListItem> GetTargetStructures() { return new List<StructureListItem>(); }
            public void RollbackCreatedStructures(IEnumerable<string> structureIds) { }
            public bool TryEnsureWriteAccess(out string error) { error = null; return true; }

            public EvaluationDoseContext ExtractEvaluationDoseContext(string planKey, SFRTParameters parameters)
            {
                return new EvaluationDoseContext
                {
                    Plan = new PlanListItem
                    {
                        Key = planKey,
                        DisplayName = "C1 / SBRT (calculated)",
                        IsDoseValid = true,
                        PrescriptionDoseGy = 20
                    },
                    Target = new StructureDoseSnapshot
                    {
                        Id = "GTV",
                        VolumeCc = 100,
                        HasDose = true,
                        D5Gy = 22,
                        D10Gy = 20,
                        D90Gy = 5,
                        D95Gy = 4
                    },
                    Peaks = new StructureDoseSnapshot
                    {
                        Id = "Lattice_Peaks",
                        VolumeCc = 3,
                        HasDose = true,
                        DmaxGy = 24,
                        DmeanGy = 20,
                        DminGy = 16,
                        D95Gy = 18,
                        D98Gy = 17,
                        V100Percent = 95
                    },
                    Valley = new StructureDoseSnapshot
                    {
                        Id = "Lattice_Valley",
                        VolumeCc = 97,
                        HasDose = true,
                        DmaxGy = 12,
                        DmeanGy = 5,
                        DminGy = 2,
                        D2Gy = 11,
                        D10Gy = 8
                    },
                    IndividualPeaks =
                    {
                        new StructureDoseSnapshot { Id = "Peak_01", VolumeCc = 0.5, DmaxGy = 24, DmeanGy = 20, DminGy = 16, HasDose = true },
                        new StructureDoseSnapshot { Id = "Peak_02", VolumeCc = 0.5, DmaxGy = 23.5, DmeanGy = 19.5, DminGy = 15.5, HasDose = true }
                    }
                };
            }
        }
    }
}

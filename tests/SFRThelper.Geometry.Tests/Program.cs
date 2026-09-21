using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            Run("FCC nearest-neighbor spacing equals d", TestFccNearestNeighbor);
            Run("COM-anchored origin is lattice (0,0,0)", TestComAnchor);
            Run("Yaw 45 deg rotates planar lattice about Z", TestYawRotation);
            Run("VoxelMask contains only occupied voxels", TestVoxelMask);
            Run("Spatial hash rejects sub-minimum distances in O(N)", TestSpatialHash);
            Run("Lattice generation stays inside V_valid", TestValidVolumeFilter);
            Run("Spacing validation requires d >= 2r", TestParameterValidation);
            Run("Directional SI spacing is independent of dxy", TestDirectionalSpacing);
            Run("Miami / Mayo / Valencia / Mini-Lattice presets", TestProtocolPresets);
            Run("TG-263 Peak ids, rings, and 16-character limit", TestStructureNaming);
            Run("Penumbra shell, Valley_Core, and ring IDs fit Eclipse 16 chars", TestTuningStructureIds);
            Run("Penumbra 1–5 mm and outer ring > inner ring", TestTuningParameterValidation);
            Run("Miami / Mayo / Mini-Lattice PO valley and penumbra fractions", TestPoObjectivePresets);
            Run("PO Build doses match Rx and protocol fractions", TestPoBuildDoses);
            Run("Seed CanExecute requires PlanSetup, peaks, valley, Rx, and Nfx", TestCanSeedGuards);
            Run("Transaction rollback is sequential reverse order", TestTransactionRollback);
            Run("PVDR and volume-fraction flags", TestMetrics);
            Run("gEUD from cumulative DVH bins", TestGeud);
            Run("Evaluation service assembles PVDR rows and alerts", TestEvaluationService);
            Run("Coarse dose grid raises the 1.25 mm alert", TestDoseGridAlert);
            Run("Feasibility uses packed count and clearances", TestFeasibility);
            Run("QA exporter writes CSV headers and PDF bytes", TestQaExport);
            Run("Halton samples the unit interval", TestHalton);
            Run("Score prefers higher mean border distance when N ties", TestMaximizationScore);
            Run("Rigid phase-shift increases sphere count vs COM phase", TestPhaseShiftMaximization);
            Run("Particle relaxation stays inside V_valid and respects spacing", TestParticleRelaxation);
            Run("EDT distance is larger at the interior than at the border", TestDistanceField);

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

        private static void TestFccNearestNeighbor()
        {
            const double d = 20.0;
            Point3D origin = SphereOptimizer.FaceCenteredCubicPoint(0, 0, 0, d);
            AssertNear(origin.DistanceTo(SphereOptimizer.FaceCenteredCubicPoint(1, 1, 0, d)), d, 1e-9, "FCC xy");
            AssertNear(origin.DistanceTo(SphereOptimizer.FaceCenteredCubicPoint(1, 0, 1, d)), d, 1e-9, "FCC xz");
            AssertNear(origin.DistanceTo(SphereOptimizer.FaceCenteredCubicPoint(0, 1, 1, d)), d, 1e-9, "FCC yz");
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

        private static void TestYawRotation()
        {
            var origin = new Point3D(0, 0, 10);
            var t = LatticeTransform.FromYawDegrees(origin, 45);
            Point3D p = t.ToPatient(Math.Sqrt(2.0), 0, 0);
            AssertNear(p.X, 1.0, 1e-9, "yaw X");
            AssertNear(p.Y, 1.0, 1e-9, "yaw Y");
            AssertNear(p.Z, 10.0, 1e-12, "yaw Z unchanged");
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
                SelectedTargetId = "GTV",
                EnableSphereMaximization = false
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

        private static void TestDirectionalSpacing()
        {
            var p = new SFRTParameters
            {
                SelectedTargetId = "GTV",
                SphereDiameterMm = 10,
                CenterSpacingMm = 20,
                IsDirectionalSpacing = true,
                LateralSpacingMm = 22,
                SiSpacingMm = 25
            };
            AssertTrue(p.IsValid, "directional spacing >= 2r");
            AssertNear(p.EffectiveLateralSpacingMm, 22, 1e-9, "dxy");
            AssertNear(p.EffectiveSiSpacingMm, 25, 1e-9, "dz");
            p.SiSpacingMm = 9;
            AssertTrue(!p.IsValid, "SI pitch < 2r is invalid");
        }

        private static void TestProtocolPresets()
        {
            var p = new SFRTParameters { SelectedTargetId = "GTV" };
            SFRTProtocolPresets.Apply(p, ClinicalProtocolPreset.UniversityOfMiami);
            AssertNear(p.SphereDiameterMm, 15, 1e-9, "Miami D");
            AssertNear(p.CenterSpacingMm, 30, 1e-9, "Miami spacing");
            AssertNear(p.TargetInternalMarginMm, 10, 1e-9, "Miami margin");
            AssertTrue(p.Preset == ClinicalProtocolPreset.UniversityOfMiami, "Miami preset id");
            AssertTrue(p.PoObjectivePreset == PoObjectivePreset.UniversityOfMiami, "Miami PO preset");
            AssertNear(p.ValleyUpperPercentOfRx, 30, 1e-9, "Miami valley 30%");

            SFRTProtocolPresets.Apply(p, ClinicalProtocolPreset.MayoClinic);
            AssertNear(p.SphereDiameterMm, 10, 1e-9, "Mayo D");
            AssertNear(p.CenterSpacingMm, 25, 1e-9, "Mayo spacing");

            SFRTProtocolPresets.Apply(p, ClinicalProtocolPreset.Valencia);
            AssertNear(p.SphereDiameterMm, 10, 1e-9, "Valencia D");
            AssertNear(p.CenterSpacingMm, 20, 1e-9, "Valencia spacing");
            AssertNear(p.TargetInternalMarginMm, 5, 1e-9, "Valencia margin");

            SFRTProtocolPresets.Apply(p, ClinicalProtocolPreset.MiniLattice);
            AssertNear(p.SphereDiameterMm, 6, 1e-9, "Mini D");
            AssertNear(p.CenterSpacingMm, 12, 1e-9, "Mini spacing");
            AssertTrue(p.PoObjectivePreset == PoObjectivePreset.MiniLattice, "Mini PO preset follows geometry");
            AssertNear(p.ValleyUpperPercentOfRx, 40, 1e-9, "Mini valley 40%");

            p.SphereDiameterMm = 8;
            AssertTrue(p.Preset == ClinicalProtocolPreset.Custom, "edit marks Custom");
        }

        private static void TestStructureNaming()
        {
            AssertTrue(StructureNaming.FormatPeakId(1) == "Peak_01", "Peak_01");
            AssertTrue(StructureNaming.FormatPeakId(12) == "Peak_12", "Peak_12");
            AssertTrue(StructureNaming.IsIndividualPeakId("Peak_03"), "regex Peak_03");
            AssertTrue(!StructureNaming.IsIndividualPeakId("Lattice_Peaks"), "composite is not individual");
            AssertTrue(StructureNaming.CompositePeaksId.Length <= StructureNaming.MaxIdLength, "Lattice_Peaks length");
            AssertTrue(StructureNaming.ValleyId.Length <= StructureNaming.MaxIdLength, "Lattice_Valley length");
            AssertTrue(StructureNaming.Ring01Id == "Ring_SFRT_0-1cm", "ring 0-1");
            AssertTrue(StructureNaming.Ring13Id == "Ring_SFRT_1-3cm", "ring 1-3");
            AssertTrue(StructureNaming.Ring01Id.Length <= 16, "ring 0-1 length");
            AssertTrue(StructureNaming.Ring13Id.Length <= 16, "ring 1-3 length");
            AssertTrue(StructureNaming.PenumbraShellId == "Peak_Penumbra", "Peak_Penumbra_Shell truncated");
            AssertTrue(StructureNaming.PenumbraShellId.Length <= StructureNaming.MaxIdLength, "Peak_Penumbra length");
            AssertTrue(StructureNaming.ValleyCoreId == "Valley_Core", "Valley_Core id");
            AssertTrue(StructureNaming.ValleyCoreId.Length <= StructureNaming.MaxIdLength, "Valley_Core length");
            AssertTrue("Peak_Penumbra_Shell".Length > StructureNaming.MaxIdLength, "full penumbra name exceeds Eclipse");
            var existing = new HashSet<string> { "Lattice_Peaks" };
            string next = StructureNaming.NextAvailable("Lattice_Peaks", existing);
            AssertTrue(next != "Lattice_Peaks", "unique id");
            AssertTrue(next.Length <= 16, "unique id length");
        }

        private static void TestTuningStructureIds()
        {
            AssertTrue(StructureNaming.PenumbraShellId == "Peak_Penumbra", "clinical 16-char Peak_Penumbra");
            AssertTrue(StructureNaming.PenumbraShellId.Length <= StructureNaming.MaxIdLength, "Peak_Penumbra fits");
            AssertTrue(StructureNaming.Truncate("Peak_Penumbra_Shell").Length == StructureNaming.MaxIdLength, "raw Peak_Penumbra_Shell truncates to 16");
            AssertTrue(StructureNaming.DicomControl == "CONTROL", "CONTROL type");
            AssertTrue(StructureNaming.DicomAvoidance == "AVOIDANCE", "AVOIDANCE type");
        }

        private static void TestTuningParameterValidation()
        {
            var p = new SFRTParameters { SelectedTargetId = "GTV", SphereRadiusMm = 5, CenterSpacingMm = 15 };
            AssertTrue(p.GenerateTuningStructures, "tuning on by default");
            AssertNear(p.PenumbraShellThicknessMm, 3.0, 1e-9, "default shell");
            AssertNear(p.ConcentricRing1Mm, 10.0, 1e-9, "default ring1");
            AssertNear(p.ConcentricRing2Mm, 30.0, 1e-9, "default ring2");
            AssertTrue(p.IsValid, "defaults valid");

            p.PenumbraShellThicknessMm = 0.5;
            AssertTrue(!p.IsValid, "shell < 1 mm");
            p.PenumbraShellThicknessMm = 6.0;
            AssertTrue(!p.IsValid, "shell > 5 mm");
            p.PenumbraShellThicknessMm = 3.0;
            AssertTrue(p.IsValid, "shell restored");

            p.ConcentricRing2Mm = 8.0;
            AssertTrue(!p.IsValid, "ring2 must exceed ring1");
            p.ConcentricRing2Mm = 30.0;
            AssertTrue(p.IsValid, "rings restored");

            p.PrescriptionDoseGy = 0;
            AssertTrue(!p.IsValid, "Rx must be > 0");
            p.PrescriptionDoseGy = 20;
            p.FractionCount = 0;
            AssertTrue(!p.IsValid, "Nfx must be >= 1");
            p.FractionCount = 1;
            AssertTrue(p.IsValid, "Rx/fx restored");
        }

        private static void TestPoObjectivePresets()
        {
            var p = new SFRTParameters { SelectedTargetId = "GTV" };
            OptimizationObjectivePresets.Apply(p, PoObjectivePreset.UniversityOfMiami);
            AssertNear(p.ValleyUpperPercentOfRx, 30, 1e-9, "Miami valley 30");
            AssertNear(p.PenumbraUpperPercentOfRx, 55, 1e-9, "Miami penumbra 55");

            OptimizationObjectivePresets.Apply(p, PoObjectivePreset.MayoClinic);
            AssertNear(p.ValleyUpperPercentOfRx, 35, 1e-9, "Mayo valley 35");
            AssertNear(p.PenumbraUpperPercentOfRx, 60, 1e-9, "Mayo penumbra 60");

            OptimizationObjectivePresets.Apply(p, PoObjectivePreset.Valencia);
            AssertNear(p.ValleyUpperPercentOfRx, 30, 1e-9, "Valencia valley 30");

            OptimizationObjectivePresets.Apply(p, PoObjectivePreset.MiniLattice);
            AssertNear(p.ValleyUpperPercentOfRx, 40, 1e-9, "Mini valley 40");
            AssertNear(p.PenumbraUpperPercentOfRx, 60, 1e-9, "Mini penumbra 60");
        }

        private static void TestPoBuildDoses()
        {
            var p = new SFRTParameters { SelectedTargetId = "GTV" };
            p.PrescriptionDoseGy = 20.0;
            p.FractionCount = 1;
            OptimizationObjectivePresets.Apply(p, PoObjectivePreset.UniversityOfMiami);
            IList<PoPointObjective> miami = OptimizationObjectivePresets.Build(p);
            PoPointObjective peaks = miami.First(o => o.Role == "Lattice_Peaks");
            PoPointObjective valley = miami.First(o => o.Role == "Valley_Core");
            PoPointObjective shell = miami.First(o => o.Role == "Peak_Penumbra_Shell");
            PoPointObjective ring01 = miami.First(o => o.Role == "Ring_SFRT_0-1cm");
            PoPointObjective ring13 = miami.First(o => o.Role == "Ring_SFRT_1-3cm");
            AssertTrue(peaks.IsLower && peaks.VolumePercent == 99.0 && peaks.Priority == 120, "peaks lower V99 pri 120");
            AssertNear(peaks.DoseGy, 20.0, 1e-9, "peaks 100% Rx");
            AssertNear(valley.DoseGy, 6.0, 1e-9, "Miami valley 30% of 20");
            AssertTrue(!valley.IsLower && valley.VolumePercent == 0.0, "valley upper V0");
            AssertNear(shell.DoseGy, 11.0, 1e-9, "Miami penumbra 55% of 20");
            AssertNear(ring01.DoseGy, 9.0, 1e-9, "ring 0-1 45% of 20");
            AssertNear(ring13.DoseGy, 5.0, 1e-9, "ring 1-3 25% of 20");

            OptimizationObjectivePresets.Apply(p, PoObjectivePreset.MayoClinic);
            IList<PoPointObjective> mayo = OptimizationObjectivePresets.Build(p);
            AssertNear(mayo.First(o => o.Role == "Valley_Core").DoseGy, 7.0, 1e-9, "Mayo valley 35% of 20");
            AssertNear(mayo.First(o => o.Role == "Peak_Penumbra_Shell").DoseGy, 12.0, 1e-9, "Mayo penumbra 60% of 20");

            OptimizationObjectivePresets.Apply(p, PoObjectivePreset.MiniLattice);
            IList<PoPointObjective> mini = OptimizationObjectivePresets.Build(p);
            AssertNear(mini.First(o => o.Role == "Valley_Core").DoseGy, 8.0, 1e-9, "Mini valley 40% of 20");

            p.Oar1StructureId = "Cord";
            p.Oar1DoseLimitGy = 12.0;
            IList<PoPointObjective> withOar = OptimizationObjectivePresets.Build(p);
            PoPointObjective oar = withOar.First(o => o.Role == "OAR Avoidance 1");
            AssertNear(oar.DoseGy, 12.0, 1e-9, "OAR Dmax");
            AssertTrue(!oar.IsLower && oar.VolumePercent == 0.0, "OAR upper V0");
        }

        private static void TestCanSeedGuards()
        {
            var p = new SFRTParameters { SelectedTargetId = "GTV", PrescriptionDoseGy = 20, FractionCount = 1 };
            AssertTrue(OptimizationObjectivePresets.CanSeed(p, true, true, true), "all guards met");
            AssertTrue(!OptimizationObjectivePresets.CanSeed(p, false, true, true), "needs PlanSetup");
            AssertTrue(!OptimizationObjectivePresets.CanSeed(p, true, false, true), "needs Lattice_Peaks");
            AssertTrue(!OptimizationObjectivePresets.CanSeed(p, true, true, false), "needs Valley_Core or Lattice_Valley");
            p.PrescriptionDoseGy = 0;
            AssertTrue(!OptimizationObjectivePresets.CanSeed(p, true, true, true), "needs Rx > 0");
            p.PrescriptionDoseGy = 20;
            p.FractionCount = 0;
            AssertTrue(!OptimizationObjectivePresets.CanSeed(p, true, true, true), "needs Nfx >= 1");
            AssertTrue(!OptimizationObjectivePresets.CanSeed(null, true, true, true), "null parameters");
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
            tx.Untrack("Lattice_Peaks");
            var after = tx.RollbackOrder().ToList();
            AssertTrue(after.Count == 2 && after[0] == "Peak_02", "untrack removes from rollback");
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

        private static void TestGeud()
        {
            var bins = new List<DvhBin>
            {
                new DvhBin { DoseGy = 8.0, CumulativeVolume = 100 },
                new DvhBin { DoseGy = 8.0, CumulativeVolume = 0 }
            };
            AssertNear(SfrtMetricsCalculator.Geud(bins, 1), 8.0, 1e-6, "gEUD a=1");
            AssertNear(SfrtMetricsCalculator.Geud(bins, 2), 8.0, 1e-6, "gEUD a=2");
            AssertNear(SfrtMetricsCalculator.Geud(bins, -10), 8.0, 1e-6, "gEUD a=-10");
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
            AssertNear(result.TargetGeudAMinus10, 18.0, 1e-9, "target gEUD");
            AssertNear(result.ValleyGeudA1, 5.0, 1e-9, "valley gEUD a=1");
            AssertTrue(result.MetricRows.Any(r => r.Metric == "gEUD a=-10"), "gEUD row");
        }

        private static void TestDoseGridAlert()
        {
            var esapi = new FakeEsapi { DoseGridMm = 2.5, TotalMu = 12000 };
            var svc = new SFRTEvaluationService(esapi);
            SFRTEvaluationResult result = svc.Evaluate("PlanSetup|C1|SBRT", new SFRTParameters { SelectedTargetId = "GTV" });
            AssertTrue(result.DoseGridCoarse, "coarse grid");
            AssertTrue(result.Alerts.Any(a => a.IndexOf("1.25 mm", StringComparison.Ordinal) >= 0), "grid alert text");
            AssertTrue(result.Overmodulated, "MU/Gy overmodulation");
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

        private static void TestQaExport()
        {
            var result = new SFRTEvaluationResult
            {
                Success = true,
                PvdrMean = 4,
                VolumeFractionPercent = 3,
                PlanDisplayName = "C1 / SBRT"
            };
            result.MetricRows.Add(new EvaluationMetricRow
            {
                Category = "PVDR",
                Structure = "Lattice_Peaks",
                Metric = "PVDR_mean",
                Absolute = "4.00",
                Relative = "OK",
                Comment = "test"
            });
            string dir = Path.Combine(Path.GetTempPath(), "nsfrt-qa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string csvPath = Path.Combine(dir, "report.csv");
            QaReportExporter.Export(result, new SFRTParameters { SelectedTargetId = "GTV" }, csvPath);
            AssertTrue(File.Exists(csvPath), "csv exists");
            AssertTrue(File.Exists(Path.Combine(dir, "report.pdf")), "pdf exists");
            string csv = File.ReadAllText(csvPath);
            AssertTrue(csv.Contains("PVDR_mean"), "csv metric");
            AssertTrue(File.ReadAllBytes(Path.Combine(dir, "report.pdf")).Length > 20, "pdf bytes");
        }

        private static void TestHalton()
        {
            double h = SphereOptimizer.Halton(0, 2);
            AssertTrue(h > 0 && h < 1, "Halton in (0,1)");
            AssertTrue(Math.Abs(SphereOptimizer.Halton(0, 2) - SphereOptimizer.Halton(1, 2)) > 1e-9, "distinct samples");
        }

        private static void TestMaximizationScore()
        {
            double a = SphereOptimizer.Score(7, 8.0);
            double b = SphereOptimizer.Score(7, 2.0);
            AssertTrue(a > b, "tie-break on mean distance");
            AssertTrue(SphereOptimizer.Score(8, 0) > a, "count dominates distance");
        }

        private static LatticeGeometryContext OffsetBoxGeometry()
        {
            // V_valid [1,29]^3 mm, COM at (15,15,15). Unshifted cubic d=15 keeps only the COM.
            var mask = new VoxelMask(1, 1, 1, 1, 1, 1, 28, 28, 28);
            for (int iz = 0; iz < 28; iz++)
                for (int iy = 0; iy < 28; iy++)
                    for (int ix = 0; ix < 28; ix++)
                        mask.Set(ix, iy, iz, true);
            mask.ComputeDistanceField();
            var com = new Point3D(15, 15, 15);
            return new LatticeGeometryContext
            {
                CenterOfMass = com,
                TargetBounds = BoundingBox3D.FromMinMax(1, 1, 1, 29, 29, 29),
                ValidVolume = mask,
                TargetVolumeCc = 22,
                ValidVolumeCc = mask.VolumeCc,
                Transform = LatticeTransform.Identity(com)
            };
        }

        private static SFRTParameters CubicOffsetParameters(bool maximize, SphereMaximizationStrategy strategy)
        {
            return new SFRTParameters
            {
                SelectedTargetId = "GTV",
                SphereRadiusMm = 5,
                CenterSpacingMm = 15,
                PackingMode = PackingGeometryMode.SimpleCubic,
                MaxSphereCount = 50,
                GridRotationDeg = 0,
                EnableSphereMaximization = maximize,
                MaxIterations = 40,
                OptimizationStrategy = strategy
            };
        }

        private static void TestPhaseShiftMaximization()
        {
            var geometry = OffsetBoxGeometry();
            var optimizer = new SphereOptimizer();
            LatticePackingResult baseline = optimizer.GenerateLattice(geometry, CubicOffsetParameters(false, SphereMaximizationStrategy.RigidPhaseShift));
            LatticePackingResult optimized = optimizer.GenerateLattice(geometry, CubicOffsetParameters(true, SphereMaximizationStrategy.RigidPhaseShift));
            AssertTrue(baseline.SphereCount >= 1, "baseline at least COM");
            AssertTrue(optimized.OptimizedCount > baseline.SphereCount, "phase shift should add spheres");
            AssertTrue(optimized.SphereCountGain == optimized.OptimizedCount - optimized.BaselineCount, "gain math");
            foreach (var sphere in optimized.Spheres)
                AssertTrue(geometry.ValidVolume.Contains(sphere.Center), "optimized center outside V_valid");
        }

        private static void TestParticleRelaxation()
        {
            var geometry = OffsetBoxGeometry();
            var optimizer = new SphereOptimizer();
            LatticePackingResult result = optimizer.GenerateLattice(
                geometry, CubicOffsetParameters(true, SphereMaximizationStrategy.ParticleRelaxation));
            AssertTrue(result.SphereCount >= result.BaselineCount, "relaxation never worse than baseline");
            var hash = new SpatialHashGrid(15);
            foreach (var sphere in result.Spheres)
            {
                AssertTrue(geometry.ValidVolume.Contains(sphere.Center), "relaxed center outside V_valid");
                AssertTrue(!hash.HasNeighborWithin(sphere.Center, 15), "spacing violation");
                hash.Add(sphere.Center);
            }
        }

        private static void TestDistanceField()
        {
            var mask = new VoxelMask(0, 0, 0, 1, 1, 1, 11, 11, 11);
            for (int iz = 0; iz < 11; iz++)
                for (int iy = 0; iy < 11; iy++)
                    for (int ix = 0; ix < 11; ix++)
                        mask.Set(ix, iy, iz, true);
            mask.ComputeDistanceField();
            double border = mask.DistanceToBoundaryMm(new Point3D(0.5, 5.5, 5.5));
            double interior = mask.DistanceToBoundaryMm(new Point3D(5.5, 5.5, 5.5));
            AssertTrue(interior > border + 1.0, "interior farther from boundary than the face voxel");
            AssertTrue(!mask.Contains(new Point3D(-1, 5, 5)), "outside");
            AssertNear(mask.DistanceToBoundaryMm(new Point3D(-1, 5, 5)), 0, 1e-9, "outside distance");
        }

        private sealed class FakeEsapi : IESAPIService
        {
            public double DoseGridMm { get; set; }
            public double TotalMu { get; set; }
            public bool IsStandalone { get { return false; } }
            public IEsapiWorker Worker { get { return null; } }
            public string SetupVmatArcs() { return "ok"; }
            public string SeedPhotonObjectives(SFRTParameters parameters) { return "ok"; }
            public bool HasPhotonSeedingStructures() { return true; }
            public bool HasActivePlanSetup() { return true; }
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
                        D95Gy = 4,
                        GeudAMinus10 = 18
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
                        D10Gy = 8,
                        GeudA1 = 5,
                        GeudA2 = 6
                    },
                    IndividualPeaks =
                    {
                        new StructureDoseSnapshot { Id = "Peak_01", VolumeCc = 0.5, DmaxGy = 24, DmeanGy = 20, DminGy = 16, HasDose = true },
                        new StructureDoseSnapshot { Id = "Peak_02", VolumeCc = 0.5, DmaxGy = 23.5, DmeanGy = 19.5, DminGy = 15.5, HasDose = true }
                    },
                    DoseGridXMm = DoseGridMm,
                    DoseGridYMm = DoseGridMm,
                    DoseGridZMm = DoseGridMm,
                    TotalMu = TotalMu
                };
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace SFRThelper.Models
{
    public class PoPointObjective
    {
        public string Role { get; set; }
        public string StructureId { get; set; }
        public string StructurePrefix { get; set; }
        public bool IsLower { get; set; }
        public double DoseGy { get; set; }
        public double VolumePercent { get; set; }
        public double Priority { get; set; }
        public bool Required { get; set; }
    }

    public sealed class PoObjectiveProfile
    {
        public PoObjectivePreset Id { get; set; }
        public string DisplayName { get; set; }
        public double ValleyUpperFraction { get; set; }
        /// <summary>Peak_Penumbra / Grams z_IC upper dose as fraction of Rx.</summary>
        public double PenumbraUpperFraction { get; set; }
        /// <summary>Peak_Penu2 / Grams z_MC upper dose as fraction of Rx (typically ~0.50).</summary>
        public double MiddleShellUpperFraction { get; set; }
        public double Ring01UpperFraction { get; set; }
        public double Ring13UpperFraction { get; set; }
        /// <summary>
        /// Peak Lower volume %. Institutional protocols often use ~99%;
        /// Grams VMAT Grid intentionally undercovers at ~50% so dose collapses into the core.
        /// </summary>
        public double PeakVolumePercent { get; set; }
        public double PeakPriority { get; set; }
        public double ValleyPriority { get; set; }
        public double PenumbraPriority { get; set; }
        public double MiddleShellPriority { get; set; }
        public double Ring01Priority { get; set; }
        public double Ring13Priority { get; set; }
        public double OarPriority { get; set; }
        /// <summary>When true, seed Lower on PeakCore_/Lattice_Cores in addition to Lattice_Peaks.</summary>
        public bool PreferCoreLowerObjectives { get; set; }
        /// <summary>When true, seed Lower on each Peak_xx (Grams individual-sphere workflow).</summary>
        public bool PreferIndividualPeakObjectives { get; set; }
    }

    /// <summary>
    /// Protocol-tailored Photon Optimizer point objectives. Pure POCOs — no VMS types.
    /// Concentric vertex-shell fractions follow Grams et al. (PRO 2021) and automated
    /// lattice planning (Med Phys 2023): z_IC / z_MC / z_OC with control priorities ≥ peaks.
    /// </summary>
    public static class OptimizationObjectivePresets
    {
        public static readonly PoObjectiveProfile[] All =
        {
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.UniversityOfMiami,
                DisplayName = "Miami [100% / 30%]",
                ValleyUpperFraction = 0.30,
                PenumbraUpperFraction = 0.95,
                MiddleShellUpperFraction = 0.50,
                Ring01UpperFraction = 0.40,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 100,
                ValleyPriority = 110,
                PenumbraPriority = 125,
                MiddleShellPriority = 125,
                Ring01Priority = 110,
                Ring13Priority = 100,
                OarPriority = 120,
                PreferCoreLowerObjectives = true,
                PreferIndividualPeakObjectives = true
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.MayoClinic,
                DisplayName = "Mayo [100% / 35%]",
                ValleyUpperFraction = 0.35,
                PenumbraUpperFraction = 0.95,
                MiddleShellUpperFraction = 0.50,
                Ring01UpperFraction = 0.40,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 100,
                ValleyPriority = 110,
                PenumbraPriority = 125,
                MiddleShellPriority = 125,
                Ring01Priority = 110,
                Ring13Priority = 100,
                OarPriority = 120,
                PreferCoreLowerObjectives = true,
                PreferIndividualPeakObjectives = true
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.Valencia,
                DisplayName = "Valencia [100% / 30%]",
                ValleyUpperFraction = 0.30,
                PenumbraUpperFraction = 0.95,
                MiddleShellUpperFraction = 0.50,
                Ring01UpperFraction = 0.40,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 100,
                ValleyPriority = 110,
                PenumbraPriority = 125,
                MiddleShellPriority = 125,
                Ring01Priority = 110,
                Ring13Priority = 100,
                OarPriority = 120,
                PreferCoreLowerObjectives = true,
                PreferIndividualPeakObjectives = true
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.MiniLattice,
                DisplayName = "Mini-Lattice [100% / 40%]",
                ValleyUpperFraction = 0.40,
                PenumbraUpperFraction = 0.95,
                MiddleShellUpperFraction = 0.50,
                Ring01UpperFraction = 0.40,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 100,
                ValleyPriority = 100,
                PenumbraPriority = 125,
                MiddleShellPriority = 125,
                Ring01Priority = 110,
                Ring13Priority = 100,
                OarPriority = 120,
                PreferCoreLowerObjectives = true,
                PreferIndividualPeakObjectives = true
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.VmatGrid,
                DisplayName = "VMAT Grid / Grams [V50% / shells]",
                ValleyUpperFraction = 0.35,
                PenumbraUpperFraction = 0.98,
                MiddleShellUpperFraction = 0.50,
                Ring01UpperFraction = 0.40,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 50.0,
                PeakPriority = 100,
                ValleyPriority = 125,
                PenumbraPriority = 150,
                MiddleShellPriority = 150,
                Ring01Priority = 150,
                Ring13Priority = 150,
                OarPriority = 125,
                PreferCoreLowerObjectives = true,
                PreferIndividualPeakObjectives = true
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.Custom,
                DisplayName = "Custom",
                ValleyUpperFraction = 0.33,
                PenumbraUpperFraction = 0.95,
                MiddleShellUpperFraction = 0.50,
                Ring01UpperFraction = 0.40,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 100,
                ValleyPriority = 110,
                PenumbraPriority = 125,
                MiddleShellPriority = 125,
                Ring01Priority = 110,
                Ring13Priority = 100,
                OarPriority = 120,
                PreferCoreLowerObjectives = true,
                PreferIndividualPeakObjectives = true
            }
        };

        public static PoObjectiveProfile Find(PoObjectivePreset id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id)
                    return All[i];
            }
            return All[All.Length - 1];
        }

        public static void Apply(SFRTParameters parameters, PoObjectivePreset id)
        {
            if (parameters == null)
                throw new ArgumentNullException("parameters");
            PoObjectiveProfile profile = Find(id);
            parameters.BeginPresetApply();
            try
            {
                parameters.PoObjectivePreset = id;
                parameters.ValleyUpperPercentOfRx = profile.ValleyUpperFraction * 100.0;
                parameters.PenumbraUpperPercentOfRx = profile.PenumbraUpperFraction * 100.0;
            }
            finally
            {
                parameters.EndPresetApply();
            }
        }

        public static bool CanSeed(SFRTParameters parameters, bool hasPlanSetup, bool hasPeaks, bool hasValleyCoreOrValley)
        {
            if (!hasPlanSetup || !hasPeaks || !hasValleyCoreOrValley || parameters == null)
                return false;
            return parameters.PrescriptionDoseGy > 0 && parameters.FractionCount >= 1;
        }

        public static IList<PoPointObjective> Build(SFRTParameters parameters)
        {
            double rx = parameters != null ? parameters.PrescriptionDoseGy : 0;
            return Build(parameters, rx);
        }

        public static IList<PoPointObjective> Build(SFRTParameters parameters, double prescriptionDoseGy)
        {
            var list = new List<PoPointObjective>();
            if (parameters == null)
                return list;

            PoObjectiveProfile profile = Find(parameters.PoObjectivePreset);
            double rx = prescriptionDoseGy;
            if (rx <= 0)
                rx = 20.0;

            double valleyFrac = parameters.ValleyUpperPercentOfRx / 100.0;
            if (valleyFrac <= 0)
                valleyFrac = profile.ValleyUpperFraction;
            double penumbraFrac = parameters.PenumbraUpperPercentOfRx / 100.0;
            if (penumbraFrac <= 0)
                penumbraFrac = profile.PenumbraUpperFraction;
            double middleFrac = profile.MiddleShellUpperFraction;
            if (middleFrac <= 0)
                middleFrac = 0.50;

            // Composite peaks (always). Individual Peak_xx / PeakCore added by seeder when present.
            list.Add(new PoPointObjective
            {
                Role = "Lattice_Peaks",
                StructureId = StructureNaming.CompositePeaksId,
                StructurePrefix = "Lattice_Pe",
                IsLower = true,
                DoseGy = rx,
                VolumePercent = profile.PeakVolumePercent,
                Priority = profile.PeakPriority,
                Required = true
            });

            if (profile.PreferCoreLowerObjectives)
            {
                list.Add(new PoPointObjective
                {
                    Role = "Lattice_Cores",
                    StructureId = StructureNaming.CompositeCoresId,
                    StructurePrefix = "Lattice_Co",
                    IsLower = true,
                    DoseGy = rx,
                    VolumePercent = Math.Max(profile.PeakVolumePercent, 95.0),
                    Priority = profile.PeakPriority + 20,
                    Required = false
                });
            }

            list.Add(new PoPointObjective
            {
                Role = "Valley_Core",
                StructureId = StructureNaming.ValleyCoreId,
                StructurePrefix = "Valley_Co",
                IsLower = false,
                DoseGy = valleyFrac * rx,
                VolumePercent = 0.0,
                Priority = profile.ValleyPriority,
                Required = false
            });
            list.Add(new PoPointObjective
            {
                Role = "Lattice_Valley",
                StructureId = StructureNaming.ValleyId,
                StructurePrefix = "Lattice_Va",
                IsLower = false,
                DoseGy = valleyFrac * rx,
                VolumePercent = 0.0,
                Priority = profile.ValleyPriority,
                Required = false
            });

            // Grams z_IC: steep inner shell — Upper V0 at ~95–98% Rx, high priority.
            list.Add(new PoPointObjective
            {
                Role = "Peak_Penumbra_Shell",
                StructureId = StructureNaming.PenumbraShellId,
                StructurePrefix = "Peak_Penu",
                IsLower = false,
                DoseGy = penumbraFrac * rx,
                VolumePercent = 0.0,
                Priority = profile.PenumbraPriority,
                Required = false
            });

            // Grams z_MC role: second ring / Peak_Penu2 — Upper V0 at ~50% Rx.
            list.Add(new PoPointObjective
            {
                Role = "Peak_Penumbra_2",
                StructureId = StructureNaming.PenumbraShell2Id,
                StructurePrefix = "Peak_Penu2",
                IsLower = false,
                DoseGy = middleFrac * rx,
                VolumePercent = 0.0,
                Priority = profile.MiddleShellPriority,
                Required = false
            });

            // Target-centric rings (institutional SFRT) — role analogous to z_OC outside GTV.
            list.Add(new PoPointObjective
            {
                Role = "Ring_SFRT_0-1cm",
                StructureId = StructureNaming.Ring01Id,
                StructurePrefix = "Ring_SFRT_0",
                IsLower = false,
                DoseGy = profile.Ring01UpperFraction * rx,
                VolumePercent = 0.0,
                Priority = profile.Ring01Priority,
                Required = false
            });
            list.Add(new PoPointObjective
            {
                Role = "Ring_SFRT_1-3cm",
                StructureId = StructureNaming.Ring13Id,
                StructurePrefix = "Ring_SFRT_1",
                IsLower = false,
                DoseGy = profile.Ring13UpperFraction * rx,
                VolumePercent = 0.0,
                Priority = profile.Ring13Priority,
                Required = false
            });

            int oarIndex = 1;
            foreach (AvoidanceOarEntry oar in parameters.SelectedAvoidanceOars)
            {
                if (oar.DoseLimitGy <= 0)
                    continue;
                list.Add(new PoPointObjective
                {
                    Role = "OAR Avoidance " + oarIndex,
                    StructureId = oar.StructureId,
                    StructurePrefix = oar.StructureId,
                    IsLower = false,
                    DoseGy = oar.DoseLimitGy,
                    VolumePercent = 0.0,
                    Priority = profile.OarPriority,
                    Required = false
                });
                oarIndex++;
            }

            return list;
        }

        /// <summary>
        /// Grams individual-sphere Lower objectives (Peak_01…Peak_nn) at the same V%/Rx as Lattice_Peaks.
        /// </summary>
        public static PoPointObjective BuildIndividualPeakObjective(PoObjectiveProfile profile, double rxGy, string peakId)
        {
            if (profile == null)
                profile = Find(PoObjectivePreset.Custom);
            return new PoPointObjective
            {
                Role = "Individual_Peak",
                StructureId = peakId,
                StructurePrefix = peakId,
                IsLower = true,
                DoseGy = rxGy,
                VolumePercent = profile.PeakVolumePercent,
                Priority = profile.PeakPriority,
                Required = false
            };
        }
    }
}

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
        public double PenumbraUpperFraction { get; set; }
        public double Ring01UpperFraction { get; set; }
        public double Ring13UpperFraction { get; set; }
        public double PeakVolumePercent { get; set; }
        public double PeakPriority { get; set; }
        public double ValleyPriority { get; set; }
        public double PenumbraPriority { get; set; }
        public double Ring01Priority { get; set; }
        public double Ring13Priority { get; set; }
        public double OarPriority { get; set; }
    }

    /// <summary>
    /// Protocol-tailored Photon Optimizer point objectives. Pure POCOs — no VMS types.
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
                PenumbraUpperFraction = 0.55,
                Ring01UpperFraction = 0.45,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 120,
                ValleyPriority = 90,
                PenumbraPriority = 70,
                Ring01Priority = 60,
                Ring13Priority = 50,
                OarPriority = 80
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.MayoClinic,
                DisplayName = "Mayo [100% / 35%]",
                ValleyUpperFraction = 0.35,
                PenumbraUpperFraction = 0.60,
                Ring01UpperFraction = 0.45,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 120,
                ValleyPriority = 85,
                PenumbraPriority = 70,
                Ring01Priority = 60,
                Ring13Priority = 50,
                OarPriority = 80
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.Valencia,
                DisplayName = "Valencia [100% / 30%]",
                ValleyUpperFraction = 0.30,
                PenumbraUpperFraction = 0.55,
                Ring01UpperFraction = 0.45,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 120,
                ValleyPriority = 90,
                PenumbraPriority = 70,
                Ring01Priority = 60,
                Ring13Priority = 50,
                OarPriority = 80
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.MiniLattice,
                DisplayName = "Mini-Lattice [100% / 40%]",
                ValleyUpperFraction = 0.40,
                PenumbraUpperFraction = 0.60,
                Ring01UpperFraction = 0.45,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 120,
                ValleyPriority = 80,
                PenumbraPriority = 70,
                Ring01Priority = 60,
                Ring13Priority = 50,
                OarPriority = 80
            },
            new PoObjectiveProfile
            {
                Id = PoObjectivePreset.Custom,
                DisplayName = "Custom",
                ValleyUpperFraction = 0.33,
                PenumbraUpperFraction = 0.55,
                Ring01UpperFraction = 0.45,
                Ring13UpperFraction = 0.25,
                PeakVolumePercent = 99.0,
                PeakPriority = 120,
                ValleyPriority = 85,
                PenumbraPriority = 70,
                Ring01Priority = 60,
                Ring13Priority = 50,
                OarPriority = 80
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

            if (parameters.HasOar1 && parameters.Oar1DoseLimitGy > 0)
            {
                list.Add(new PoPointObjective
                {
                    Role = "OAR Avoidance 1",
                    StructureId = parameters.Oar1StructureId,
                    StructurePrefix = parameters.Oar1StructureId,
                    IsLower = false,
                    DoseGy = parameters.Oar1DoseLimitGy,
                    VolumePercent = 0.0,
                    Priority = profile.OarPriority,
                    Required = false
                });
            }
            if (parameters.HasOar2 && parameters.Oar2DoseLimitGy > 0)
            {
                list.Add(new PoPointObjective
                {
                    Role = "OAR Avoidance 2",
                    StructureId = parameters.Oar2StructureId,
                    StructurePrefix = parameters.Oar2StructureId,
                    IsLower = false,
                    DoseGy = parameters.Oar2DoseLimitGy,
                    VolumePercent = 0.0,
                    Priority = profile.OarPriority,
                    Required = false
                });
            }

            return list;
        }
    }
}

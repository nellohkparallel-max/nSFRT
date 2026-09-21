using System;

namespace SFRThelper.Models
{
    public sealed class SFRTProtocolPreset
    {
        public ClinicalProtocolPreset Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public double DiameterMm { get; set; }
        public double DefaultSpacingMm { get; set; }
        public double SpacingMinMm { get; set; }
        public double SpacingMaxMm { get; set; }
        public double TargetMarginMm { get; set; }
        public double TargetMarginMinMm { get; set; }
        public double TargetMarginMaxMm { get; set; }
        public PackingGeometryMode SuggestedPacking { get; set; }
    }

    public static class SFRTProtocolPresets
    {
        public static readonly SFRTProtocolPreset[] All =
        {
            new SFRTProtocolPreset
            {
                Id = ClinicalProtocolPreset.UniversityOfMiami,
                DisplayName = "University of Miami (Classic Bulky SBRT)",
                Description = "15 mm vertices, 30–60 mm pitch, ≥10 mm target margin.",
                DiameterMm = 15.0,
                DefaultSpacingMm = 30.0,
                SpacingMinMm = 30.0,
                SpacingMaxMm = 60.0,
                TargetMarginMm = 10.0,
                TargetMarginMinMm = 10.0,
                TargetMarginMaxMm = 20.0,
                SuggestedPacking = PackingGeometryMode.SimpleCubic
            },
            new SFRTProtocolPreset
            {
                Id = ClinicalProtocolPreset.MayoClinic,
                DisplayName = "Mayo Clinic",
                Description = "10 mm vertices, 25–30 mm pitch, 5–10 mm target margin.",
                DiameterMm = 10.0,
                DefaultSpacingMm = 25.0,
                SpacingMinMm = 25.0,
                SpacingMaxMm = 30.0,
                TargetMarginMm = 7.5,
                TargetMarginMinMm = 5.0,
                TargetMarginMaxMm = 10.0,
                SuggestedPacking = PackingGeometryMode.HexagonalClosePacking
            },
            new SFRTProtocolPreset
            {
                Id = ClinicalProtocolPreset.Valencia,
                DisplayName = "Valencia (VMAT External Beam SFRT)",
                Description = "10 mm vertices, 20–25 mm pitch, 5 mm target margin.",
                DiameterMm = 10.0,
                DefaultSpacingMm = 20.0,
                SpacingMinMm = 20.0,
                SpacingMaxMm = 25.0,
                TargetMarginMm = 5.0,
                TargetMarginMinMm = 5.0,
                TargetMarginMaxMm = 5.0,
                SuggestedPacking = PackingGeometryMode.HexagonalClosePacking
            },
            new SFRTProtocolPreset
            {
                Id = ClinicalProtocolPreset.MiniLattice,
                DisplayName = "Mini-Lattice (Oligomet / H&N / Paraspinal)",
                Description = "6 mm (5–8) vertices, 12–15 mm pitch, 3–5 mm target margin.",
                DiameterMm = 6.0,
                DefaultSpacingMm = 12.0,
                SpacingMinMm = 12.0,
                SpacingMaxMm = 15.0,
                TargetMarginMm = 3.0,
                TargetMarginMinMm = 3.0,
                TargetMarginMaxMm = 5.0,
                SuggestedPacking = PackingGeometryMode.FaceCenteredCubic
            },
            new SFRTProtocolPreset
            {
                Id = ClinicalProtocolPreset.Custom,
                DisplayName = "Custom",
                Description = "Fully editable geometry.",
                DiameterMm = 10.0,
                DefaultSpacingMm = 20.0,
                SpacingMinMm = 0,
                SpacingMaxMm = 0,
                TargetMarginMm = 5.0,
                TargetMarginMinMm = 0,
                TargetMarginMaxMm = 50,
                SuggestedPacking = PackingGeometryMode.HexagonalClosePacking
            }
        };

        public static SFRTProtocolPreset Find(ClinicalProtocolPreset id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id)
                    return All[i];
            }
            return All[All.Length - 1];
        }

        public static void Apply(SFRTParameters parameters, ClinicalProtocolPreset id)
        {
            if (parameters == null)
                throw new ArgumentNullException("parameters");
            SFRTProtocolPreset preset = Find(id);
            parameters.BeginPresetApply();
            try
            {
                parameters.Preset = id;
                parameters.SphereDiameterMm = preset.DiameterMm;
                parameters.CenterSpacingMm = preset.DefaultSpacingMm;
                parameters.LateralSpacingMm = preset.DefaultSpacingMm;
                parameters.SiSpacingMm = preset.DefaultSpacingMm;
                parameters.TargetClearanceMm = preset.TargetMarginMm;
                parameters.PackingMode = preset.SuggestedPacking;
                parameters.IsDirectionalSpacing = false;

                PoObjectiveProfile po = OptimizationObjectivePresets.Find((PoObjectivePreset)(int)id);
                parameters.PoObjectivePreset = po.Id;
                parameters.ValleyUpperPercentOfRx = po.ValleyUpperFraction * 100.0;
                parameters.PenumbraUpperPercentOfRx = po.PenumbraUpperFraction * 100.0;
            }
            finally
            {
                parameters.EndPresetApply();
            }
        }
    }
}

namespace SFRThelper.Models
{
    public enum PackingGeometryMode
    {
        SimpleCubic = 0,
        FaceCenteredCubic = 1,
        HexagonalClosePacking = 2
    }

    public enum SphereGenerationMode
    {
        IndividualAndComposite = 0,
        CompositeOnly = 1,
        /// <summary>
        /// Group spheres that share the same CT-slice center into one structure (LatticeHelper-style),
        /// and create −1 mm sphere-core structures per group for optimization.
        /// </summary>
        CtSliceGroupedWithCores = 2
    }

    public enum ClinicalProtocolPreset
    {
        Custom = 0,
        UniversityOfMiami = 1,
        MayoClinic = 2,
        Valencia = 3,
        MiniLattice = 4
    }

    public enum SpacingMode
    {
        Universal = 0,
        Directional = 1
    }

    public enum SphereMaximizationStrategy
    {
        RigidPhaseShift = 0,
        ParticleRelaxation = 1
    }

    public enum PoObjectivePreset
    {
        Custom = 0,
        UniversityOfMiami = 1,
        MayoClinic = 2,
        Valencia = 3,
        MiniLattice = 4,
        /// <summary>
        /// Grams et al. VMAT Grid / automated lattice (Med Phys): V50% peaks,
        /// concentric vertex shells z_IC/z_MC/z_OC, control-shell priorities above peaks.
        /// </summary>
        VmatGrid = 5
    }
}

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
        CompositeOnly = 1
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
}

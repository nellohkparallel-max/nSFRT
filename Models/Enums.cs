namespace SFRThelper.Models
{
    /// <summary>
    /// Lattice packing topology used to place peak centers relative to the target COM.
    /// </summary>
    public enum PackingGeometryMode
    {
        SimpleCubic = 0,
        HexagonalClosePacking = 1
    }

    /// <summary>
    /// Controls whether individual TG-263 Peak_xx structures are created in addition to the composite.
    /// </summary>
    public enum SphereGenerationMode
    {
        IndividualAndComposite = 0,
        CompositeOnly = 1
    }
}

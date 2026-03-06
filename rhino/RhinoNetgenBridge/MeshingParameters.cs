namespace RhinoNetgenBridge
{
    /// <summary>
    /// Parameters that control tetrahedral mesh generation via netgen.
    /// </summary>
    public sealed class MeshingParameters
    {
        // ---------------------------------------------------------------
        // Defaults match netgen's Ng_Meshing_Parameters defaults
        // ---------------------------------------------------------------

        /// <summary>
        /// Maximum allowed global mesh element size.
        /// Default is effectively unconstrained (1 × 10⁶).
        /// </summary>
        public double MaxElementSize { get; set; } = 1e6;

        /// <summary>
        /// Mesh density in the range [0, 1].
        /// 0 = coarse, 1 = fine.
        /// Default is 0.5 (medium).
        /// </summary>
        public double Fineness { get; set; } = 0.5;

        /// <summary>
        /// Grading factor in the range [0, 1].
        /// 0 = uniform mesh, 1 = aggressive local grading.
        /// Default is 0.3.
        /// </summary>
        public double Grading { get; set; } = 0.3;

        // ---------------------------------------------------------------
        // Convenience factory methods
        // ---------------------------------------------------------------

        /// <summary>Return a coarse meshing preset (fineness = 0.2).</summary>
        public static MeshingParameters Coarse() => new MeshingParameters
        {
            Fineness  = 0.2,
            Grading   = 0.3,
        };

        /// <summary>Return a medium meshing preset (fineness = 0.5).</summary>
        public static MeshingParameters Medium() => new MeshingParameters();

        /// <summary>Return a fine meshing preset (fineness = 0.8).</summary>
        public static MeshingParameters Fine() => new MeshingParameters
        {
            Fineness  = 0.8,
            Grading   = 0.3,
        };

        /// <summary>Return a very-fine meshing preset (fineness = 1.0).</summary>
        public static MeshingParameters VeryFine() => new MeshingParameters
        {
            Fineness  = 1.0,
            Grading   = 0.1,
        };
    }
}

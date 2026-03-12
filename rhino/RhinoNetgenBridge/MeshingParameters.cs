namespace RhinoNetgenBridge
{
    /// <summary>
    /// Parameters that control tetrahedral mesh generation via netgen.
    ///
    /// <para>Start from one of the factory presets (<see cref="Coarse"/>,
    /// <see cref="Medium"/>, <see cref="Fine"/>, <see cref="VeryFine"/>)
    /// and override individual properties as needed.</para>
    /// </summary>
    public sealed class MeshingParameters
    {
        // -----------------------------------------------------------------------
        // Element size
        // -----------------------------------------------------------------------

        /// <summary>
        /// Maximum allowed global mesh element size.
        /// Netgen will not create any element larger than this value.
        /// Default: 1 × 10⁶ (effectively unconstrained).
        /// </summary>
        public double MaxElementSize { get; set; } = 1e6;

        /// <summary>
        /// Minimum allowed global mesh element size.
        /// Netgen will not create any element smaller than this value.
        /// Default: 0 (no lower limit).
        /// </summary>
        public double MinElementSize { get; set; } = 0.0;

        // -----------------------------------------------------------------------
        // Density / grading
        // -----------------------------------------------------------------------

        /// <summary>
        /// Mesh density in the range [0, 1].
        /// 0 = coarse, 1 = fine.
        /// Default: 0.5 (medium).
        /// </summary>
        public double Fineness { get; set; } = 0.5;

        /// <summary>
        /// Grading factor in the range [0, 1].
        /// 0 = uniform mesh (elements of similar size everywhere).
        /// 1 = aggressive local grading (small elements near features, large elsewhere).
        /// Default: 0.3.
        /// </summary>
        public double Grading { get; set; } = 0.3;

        // -----------------------------------------------------------------------
        // Curvature / edge resolution
        // -----------------------------------------------------------------------

        /// <summary>
        /// Target number of mesh elements per geometry edge.
        /// Higher values produce a finer mesh along straight edges.
        /// Default: 2.0.
        /// </summary>
        public double ElementsPerEdge { get; set; } = 2.0;

        /// <summary>
        /// Target number of mesh elements per curvature radius.
        /// Higher values produce a finer mesh on curved surfaces.
        /// Default: 2.0.
        /// </summary>
        public double ElementsPerCurve { get; set; } = 2.0;

        // -----------------------------------------------------------------------
        // Close-edge refinement
        // -----------------------------------------------------------------------

        /// <summary>
        /// Enable automatic mesh refinement at geometrically close edges.
        /// When <c>true</c>, netgen detects narrow gaps and generates finer
        /// elements to resolve them correctly.
        /// Default: <c>false</c>.
        /// </summary>
        public bool CloseEdgeRefinement { get; set; } = false;

        /// <summary>
        /// Refinement factor used when <see cref="CloseEdgeRefinement"/> is
        /// enabled.  Larger values produce finer elements near close edges.
        /// Default: 2.0.
        /// </summary>
        public double CloseEdgeFactor { get; set; } = 2.0;

        // -----------------------------------------------------------------------
        // Minimum edge length
        // -----------------------------------------------------------------------

        /// <summary>
        /// Enforce a minimum edge length during edge subdivision.
        /// When <c>true</c>, netgen will not subdivide edges shorter than
        /// <see cref="MinEdgeLength"/>.
        /// Default: <c>false</c>.
        /// </summary>
        public bool EnforceMinEdgeLength { get; set; } = false;

        /// <summary>
        /// Minimum edge length used when <see cref="EnforceMinEdgeLength"/>
        /// is <c>true</c>.
        /// Default: 1 × 10⁻⁴.
        /// </summary>
        public double MinEdgeLength { get; set; } = 1e-4;

        // -----------------------------------------------------------------------
        // Netgen built-in optimization
        // -----------------------------------------------------------------------

        /// <summary>
        /// Enable netgen's built-in surface mesh optimisation (node smoothing,
        /// edge swapping on the surface).
        /// Default: <c>true</c>.
        /// </summary>
        public bool EnableSurfaceOptimization { get; set; } = true;

        /// <summary>
        /// Enable netgen's built-in volume mesh optimisation (node smoothing,
        /// edge/face swapping in 3-D).
        /// Default: <c>true</c>.
        /// </summary>
        public bool EnableVolumeOptimization { get; set; } = true;

        /// <summary>
        /// Number of 2-D (surface) mesh optimisation iterations.
        /// More steps improve surface mesh quality at the cost of time.
        /// Default: 3.
        /// </summary>
        public int OptimizationSteps2D { get; set; } = 3;

        /// <summary>
        /// Number of 3-D (volume) mesh optimisation iterations.
        /// More steps improve tet quality at the cost of time.
        /// Default: 3.
        /// </summary>
        public int OptimizationSteps3D { get; set; } = 3;

        // -----------------------------------------------------------------------
        // Post-generation Laplacian smoothing
        // -----------------------------------------------------------------------

        /// <summary>
        /// Number of Laplacian smoothing iterations applied to the mesh after
        /// netgen finishes generation.
        ///
        /// <para>Laplacian smoothing moves each <b>interior</b> vertex towards
        /// the weighted average of its direct tet-neighbours, making the volume
        /// mesh smoother.  Boundary (surface) vertices are never moved, so the
        /// outer shape is preserved.</para>
        ///
        /// <para>0 = disabled (default).  Values in the range 3–10 are typical.
        /// Very high iteration counts may reduce element quality near the
        /// boundary; prefer lower iteration counts combined with a higher
        /// <see cref="LaplacianSmoothingFactor"/> if needed.</para>
        /// </summary>
        public int LaplacianSmoothingIterations { get; set; } = 0;

        /// <summary>
        /// Relaxation factor λ ∈ (0, 1] for each Laplacian smoothing step.
        ///
        /// <para>At each step a vertex is moved by
        /// <c>λ × (centroid − current_position)</c>:
        /// <list type="bullet">
        ///   <item><description>
        ///     <c>1.0</c> – move fully to the centroid (fastest convergence but
        ///     can cause mesh shrinkage on convex regions).
        ///   </description></item>
        ///   <item><description>
        ///     <c>0.5</c> – half-step; conservative, rarely distorts elements
        ///     (default).
        ///   </description></item>
        ///   <item><description>
        ///     Smaller values require more iterations to achieve the same
        ///     smoothing.
        ///   </description></item>
        /// </list>
        /// </para>
        /// Default: 0.5.
        /// </summary>
        public double LaplacianSmoothingFactor { get; set; } = 0.5;

        // -----------------------------------------------------------------------
        // Second-order elements
        // -----------------------------------------------------------------------

        /// <summary>
        /// Generate second-order (TET10) tetrahedral elements instead of the
        /// default linear (TET4) elements.
        ///
        /// <para>TET10 elements add a mid-edge node on each of the 6 edges of
        /// a tetrahedron (10 nodes total per element).  They are required for
        /// quadratic-accuracy finite element analyses.</para>
        ///
        /// <para>When <c>true</c>:<br/>
        /// – <see cref="TetrahedralMesh.NodesPerElement"/> returns 10.<br/>
        /// – <see cref="TetrahedralMesh.CreateSmoothed"/> is disabled (throws).<br/>
        /// – Memory usage roughly doubles compared to TET4.</para>
        ///
        /// Default: <c>false</c>.
        /// </summary>
        public bool SecondOrder { get; set; } = false;

        // -----------------------------------------------------------------------
        // Uniform refinement
        // -----------------------------------------------------------------------

        /// <summary>
        /// Number of uniform refinement passes applied after volume meshing.
        ///
        /// <para>Each pass splits every tetrahedron into 8 child tetrahedra by
        /// bisecting all edges.  This multiplies the element count by 8 per pass,
        /// so use small values (1–2).</para>
        ///
        /// Default: 0 (disabled).
        /// </summary>
        public int UniformRefinementSteps { get; set; } = 0;

        // -----------------------------------------------------------------------
        // Factory presets
        // -----------------------------------------------------------------------

        /// <summary>Coarse preset – fast, low element count (fineness = 0.2).</summary>
        public static MeshingParameters Coarse() => new MeshingParameters
        {
            Fineness = 0.2,
            Grading  = 0.3,
        };

        /// <summary>Medium preset – balanced quality and speed (fineness = 0.5).</summary>
        public static MeshingParameters Medium() => new MeshingParameters();

        /// <summary>Fine preset – high element count (fineness = 0.8).</summary>
        public static MeshingParameters Fine() => new MeshingParameters
        {
            Fineness = 0.8,
            Grading  = 0.3,
        };

        /// <summary>Very-fine preset – maximum quality (fineness = 1.0).</summary>
        public static MeshingParameters VeryFine() => new MeshingParameters
        {
            Fineness = 1.0,
            Grading  = 0.1,
        };
    }
}

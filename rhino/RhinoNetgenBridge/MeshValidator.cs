using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// The category of a mesh issue found during validation.
    /// </summary>
    public enum MeshIssueType
    {
        /// <summary>
        /// An edge is shared by only one triangle (open boundary).
        /// Netgen requires a fully closed surface mesh.
        /// </summary>
        NakedEdge,

        /// <summary>
        /// An edge is shared by three or more triangles.
        /// The mesh is not a 2-manifold and netgen cannot process it reliably.
        /// </summary>
        NonManifoldEdge,

        /// <summary>
        /// A triangular face has zero (or near-zero) area.
        /// Degenerate faces can cause STL initialisation to fail inside netgen.
        /// </summary>
        DegenerateFace,

        /// <summary>
        /// A vertex is not referenced by any face.
        /// Unused vertices do not cause meshing to fail but indicate a
        /// poorly prepared mesh.
        /// </summary>
        UnusedVertex,
    }

    /// <summary>
    /// A single issue found in a surface mesh during validation.
    /// </summary>
    public sealed class MeshIssue
    {
        /// <summary>The category of the issue.</summary>
        public MeshIssueType Type { get; }

        /// <summary>Human-readable description.</summary>
        public string Description { get; }

        /// <summary>
        /// Zero-based index of the element (vertex, face, or edge pair) where
        /// the issue was detected, or -1 if not applicable.
        /// </summary>
        public int Index { get; }

        internal MeshIssue(MeshIssueType type, string description, int index = -1)
        {
            Type = type;
            Description = description;
            Index = index;
        }

        public override string ToString() =>
            Index >= 0
                ? $"[{Type}] {Description} (index {Index})"
                : $"[{Type}] {Description}";
    }

    /// <summary>
    /// Summary of a surface mesh validation run.
    ///
    /// <para>Use <see cref="IsValid"/> as a quick pass/fail gate.  If the
    /// mesh is invalid, inspect <see cref="Issues"/> for details and counts
    /// such as <see cref="NakedEdgeCount"/> or
    /// <see cref="NonManifoldEdgeCount"/> to decide how to proceed.</para>
    /// </summary>
    public sealed class MeshValidationResult
    {
        /// <summary>
        /// <c>true</c> if no issues that would prevent successful meshing
        /// were detected (no naked edges, no non-manifold edges, no
        /// degenerate faces).
        /// </summary>
        public bool IsValid =>
            NakedEdgeCount == 0 &&
            NonManifoldEdgeCount == 0 &&
            DegenerateFaceCount == 0;

        /// <summary>
        /// <c>true</c> if the mesh has no naked edges (is watertight).
        /// </summary>
        public bool IsClosed => NakedEdgeCount == 0;

        /// <summary>
        /// <c>true</c> if every edge is shared by exactly two faces
        /// (no non-manifold edges).
        /// </summary>
        public bool IsManifold => NonManifoldEdgeCount == 0;

        /// <summary>Number of naked (open-boundary) edges found.</summary>
        public int NakedEdgeCount { get; }

        /// <summary>Number of non-manifold edges (shared by 3+ faces) found.</summary>
        public int NonManifoldEdgeCount { get; }

        /// <summary>Number of zero-area (degenerate) faces found.</summary>
        public int DegenerateFaceCount { get; }

        /// <summary>Number of vertices not referenced by any face.</summary>
        public int UnusedVertexCount { get; }

        /// <summary>All issues detected, in the order they were found.</summary>
        public IReadOnlyList<MeshIssue> Issues { get; }

        internal MeshValidationResult(
            int nakedEdges, int nonManifoldEdges,
            int degenerateFaces, int unusedVertices,
            List<MeshIssue> issues)
        {
            NakedEdgeCount = nakedEdges;
            NonManifoldEdgeCount = nonManifoldEdges;
            DegenerateFaceCount = degenerateFaces;
            UnusedVertexCount = unusedVertices;
            Issues = issues.AsReadOnly();
        }

        public override string ToString()
        {
            if (IsValid)
                return $"Valid (unusedVerts={UnusedVertexCount})";

            return $"Invalid – nakedEdges={NakedEdgeCount}  " +
                   $"nonManifold={NonManifoldEdgeCount}  " +
                   $"degenerateFaces={DegenerateFaceCount}  " +
                   $"unusedVerts={UnusedVertexCount}";
        }
    }

    /// <summary>
    /// Validates a Rhino <see cref="Mesh"/> before it is passed to
    /// <see cref="NetgenMesher"/> for tetrahedral meshing.
    ///
    /// <para>Example:</para>
    /// <code>
    ///   MeshValidationResult result = MeshValidator.Validate(surfaceMesh);
    ///   if (!result.IsValid)
    ///   {
    ///       foreach (var issue in result.Issues)
    ///           RhinoApp.WriteLine(issue.ToString());
    ///       return;
    ///   }
    ///   TetrahedralMesh tet = NetgenMesher.GenerateFromMesh(surfaceMesh, mp);
    /// </code>
    /// </summary>
    public static class MeshValidator
    {
        private const double DegenerateAreaThreshold = 1e-20;
        private const int MaxIssueListSize = 200;

        /// <summary>
        /// Validate a surface mesh for suitability as tetrahedral mesh input.
        ///
        /// <para>Checks performed (in order):</para>
        /// <list type="number">
        ///   <item><description>Degenerate (zero-area) faces</description></item>
        ///   <item><description>Naked edges (open boundary)</description></item>
        ///   <item><description>Non-manifold edges (shared by 3+ faces)</description></item>
        ///   <item><description>Unused vertices</description></item>
        /// </list>
        ///
        /// <para>The mesh is first duplicated and quads are triangulated
        /// internally; the original mesh is never modified.</para>
        /// </summary>
        /// <param name="mesh">The surface mesh to validate.</param>
        /// <returns>
        ///   A <see cref="MeshValidationResult"/> summarising all findings.
        /// </returns>
        public static MeshValidationResult Validate(Mesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            // Triangulate quads if needed; avoid copying when the mesh is already
            // all-triangles to save memory and time.
            Mesh tri;
            if (mesh.Faces.QuadCount > 0)
            {
                tri = mesh.DuplicateMesh();
                tri.Faces.ConvertQuadsToTriangles();
            }
            else
            {
                tri = mesh;
            }

            int nv = tri.Vertices.Count;
            int nf = tri.Faces.Count;

            var issues = new List<MeshIssue>();
            int nakedEdges = 0;
            int nonManifoldEdges = 0;
            int degenerateFaces = 0;
            int unusedVertices = 0;

            // ---------------------------------------------------------------
            // 1. Degenerate faces
            // ---------------------------------------------------------------
            for (int i = 0; i < nf; ++i)
            {
                var f = tri.Faces[i];
                var v0 = tri.Vertices[f.A];
                var v1 = tri.Vertices[f.B];
                var v2 = tri.Vertices[f.C];

                double ax = v1.X - v0.X, ay = v1.Y - v0.Y, az = v1.Z - v0.Z;
                double bx = v2.X - v0.X, by = v2.Y - v0.Y, bz = v2.Z - v0.Z;
                double cx = ay * bz - az * by;
                double cy = az * bx - ax * bz;
                double cz = ax * by - ay * bx;
                double areaSq = cx * cx + cy * cy + cz * cz; // = (2A)²

                if (areaSq < DegenerateAreaThreshold)
                {
                    degenerateFaces++;
                    if (issues.Count < MaxIssueListSize)
                        issues.Add(new MeshIssue(MeshIssueType.DegenerateFace,
                            $"Face {i} has zero or near-zero area.", i));
                }
            }

            // ---------------------------------------------------------------
            // 2. Edge manifold / naked-edge check
            //    Build edge → face-count map using sorted (low, high) key.
            // ---------------------------------------------------------------
            var edgeCount = new Dictionary<long, int>(nf * 3);

            for (int i = 0; i < nf; ++i)
            {
                var f = tri.Faces[i];
                AddEdge(edgeCount, f.A, f.B);
                AddEdge(edgeCount, f.B, f.C);
                AddEdge(edgeCount, f.C, f.A);
            }

            foreach (var kv in edgeCount)
            {
                if (kv.Value == 1)
                {
                    nakedEdges++;
                    if (issues.Count < MaxIssueListSize)
                    {
                        var (a, b) = DecodeEdge(kv.Key);
                        issues.Add(new MeshIssue(MeshIssueType.NakedEdge,
                            $"Edge ({a}, {b}) is not shared – mesh is not closed."));
                    }
                }
                else if (kv.Value >= 3)
                {
                    nonManifoldEdges++;
                    if (issues.Count < MaxIssueListSize)
                    {
                        var (a, b) = DecodeEdge(kv.Key);
                        issues.Add(new MeshIssue(MeshIssueType.NonManifoldEdge,
                            $"Edge ({a}, {b}) is shared by {kv.Value} faces (non-manifold)."));
                    }
                }
            }

            // ---------------------------------------------------------------
            // 3. Unused vertices
            // ---------------------------------------------------------------
            var referenced = new bool[nv];
            for (int i = 0; i < nf; ++i)
            {
                var f = tri.Faces[i];
                referenced[f.A] = true;
                referenced[f.B] = true;
                referenced[f.C] = true;
            }
            for (int i = 0; i < nv; ++i)
            {
                if (!referenced[i])
                {
                    unusedVertices++;
                    if (issues.Count < MaxIssueListSize)
                        issues.Add(new MeshIssue(MeshIssueType.UnusedVertex,
                            $"Vertex {i} is not referenced by any face.", i));
                }
            }

            // Truncation notice
            if (nakedEdges + nonManifoldEdges + degenerateFaces + unusedVertices > MaxIssueListSize)
                issues.Add(new MeshIssue(MeshIssueType.NakedEdge,
                    "Issue list truncated at 200 entries. See counts for totals."));

            return new MeshValidationResult(
                nakedEdges, nonManifoldEdges,
                degenerateFaces, unusedVertices,
                issues);
        }

        // ---------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------

        private static void AddEdge(Dictionary<long, int> map, int a, int b)
        {
            long key = EncodeEdge(a, b);
            map[key] = map.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        private static long EncodeEdge(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        private static (int, int) DecodeEdge(long key) =>
            ((int)(key >> 32), (int)(key & 0xFFFF_FFFF));
    }
}

using System;
using System.Collections.Generic;
using Rhino.Geometry;
namespace RhinoNetgenBridge
{
    /// <summary>
    /// Stores the result of a tetrahedral meshing operation.
    ///
    /// Vertices are stored in a flat array [x0,y0,z0, x1,y1,z1, …] and
    /// tetrahedra as a flat array of 0-based vertex indices.
    /// For TET4 (linear): [a,b,c,d, …] (4 nodes per element).
    /// For TET10 (quadratic): [n0..n9, …] (10 nodes per element, corner nodes 0–3 then mid-edge nodes 4–9).
    /// </summary>
    public sealed class TetrahedralMesh
    {
        /// <summary>
        /// Flat array of vertex coordinates.
        /// Length = <see cref="VertexCount"/> * 3.
        /// Layout: [x0,y0,z0, x1,y1,z1, …]
        /// </summary>
        public double[] Vertices { get; }

        /// <summary>
        /// Flat array of tetrahedral element node indices (0-based).
        /// Length = <see cref="TetCount"/> * <see cref="NodesPerElement"/>.
        /// </summary>
        public int[] Tetrahedra { get; }

        /// <summary>
        /// Number of nodes per element.
        /// 4 for linear TET4 elements (default).
        /// 10 for quadratic TET10 elements (second-order meshing).
        /// </summary>
        public int NodesPerElement { get; }

        /// <summary>Number of vertices in the mesh.</summary>
        public int VertexCount => Vertices.Length / 3;

        /// <summary>Number of tetrahedral elements in the mesh.</summary>
        public int TetCount => Tetrahedra.Length / NodesPerElement;

        internal TetrahedralMesh(double[] vertices, int[] tetrahedra, int nodesPerElement = 4)
        {
            Vertices         = vertices   ?? throw new ArgumentNullException(nameof(vertices));
            Tetrahedra       = tetrahedra ?? throw new ArgumentNullException(nameof(tetrahedra));
            NodesPerElement  = (nodesPerElement == 4 || nodesPerElement == 10)
                ? nodesPerElement
                : throw new ArgumentOutOfRangeException(nameof(nodesPerElement),
                    "nodesPerElement must be 4 (TET4) or 10 (TET10).");
        }

        /// <summary>
        /// Return the coordinates of vertex <paramref name="index"/> as a
        /// <see cref="Point3d"/>.
        /// </summary>
        public Point3d GetVertex(int index)
        {
            if (index < 0 || index >= VertexCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            int i = index * 3;
            return new Point3d(Vertices[i], Vertices[i + 1], Vertices[i + 2]);
        }

        /// <summary>
        /// Return the four corner 0-based vertex indices of tetrahedron
        /// <paramref name="index"/> as a value tuple.
        /// For TET10, only the 4 corner nodes (indices 0–3) are returned.
        /// Use <see cref="GetTetrahedronAllNodes"/> to get all 10 nodes.
        /// </summary>
        public (int A, int B, int C, int D) GetTetrahedron(int index)
        {
            if (index < 0 || index >= TetCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            int i = index * NodesPerElement;
            return (Tetrahedra[i], Tetrahedra[i + 1], Tetrahedra[i + 2], Tetrahedra[i + 3]);
        }

        /// <summary>
        /// Return all node indices for element <paramref name="index"/>.
        /// Returns a new array of length <see cref="NodesPerElement"/>
        /// (4 for TET4, 10 for TET10).
        /// </summary>
        public int[] GetTetrahedronAllNodes(int index)
        {
            if (index < 0 || index >= TetCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            int i = index * NodesPerElement;
            var nodes = new int[NodesPerElement];
            Array.Copy(Tetrahedra, i, nodes, 0, NodesPerElement);
            return nodes;
        }

        // ---------------------------------------------------------------
        // Element quality evaluation
        // ---------------------------------------------------------------

        /// <summary>
        /// Compute quality metrics for a single tetrahedral element.
        /// </summary>
        /// <param name="index">0-based element index.</param>
        /// <returns>
        ///   A <see cref="TetQuality"/> record with mean-ratio, dihedral angles,
        ///   volume, and edge-length ratio.
        /// </returns>
        public TetQuality ComputeElementQuality(int index)
        {
            if (index < 0 || index >= TetCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            int bi = index * NodesPerElement;
            return QualityComputer.Compute(
                Vertices,
                Tetrahedra[bi],
                Tetrahedra[bi + 1],
                Tetrahedra[bi + 2],
                Tetrahedra[bi + 3]);
        }

        /// <summary>
        /// Compute quality metrics for every element in the mesh.
        /// </summary>
        /// <returns>
        ///   Array of <see cref="TetQuality"/> with one entry per element,
        ///   indexed consistently with <see cref="Tetrahedra"/>.
        /// </returns>
        public TetQuality[] ComputeAllElementQualities()
        {
            var result = new TetQuality[TetCount];
            for (int i = 0; i < TetCount; ++i)
            {
                int bi = i * NodesPerElement;
                result[i] = QualityComputer.Compute(
                    Vertices,
                    Tetrahedra[bi],
                    Tetrahedra[bi + 1],
                    Tetrahedra[bi + 2],
                    Tetrahedra[bi + 3]);
            }
            return result;
        }

        /// <summary>
        /// Compute aggregate quality statistics for the entire mesh.
        ///
        /// <para>Also returns the per-element quality array as an out parameter
        /// so that the caller can inspect individual elements without a second
        /// pass through the mesh.</para>
        /// </summary>
        /// <param name="perElementQualities">
        ///   Receives the per-element quality array (same length as
        ///   <see cref="TetCount"/>).
        /// </param>
        public MeshQualityStatistics ComputeQualityStatistics(
            out TetQuality[] perElementQualities)
        {
            perElementQualities = ComputeAllElementQualities();
            int n = perElementQualities.Length;

            if (n == 0)
                return new MeshQualityStatistics(
                    0,0,0,0, -1,-1, 0,0,0, 0,0,0, 0,0);

            double minEta = double.MaxValue, maxEta = double.MinValue, sumEta = 0;
            double minDih = double.MaxValue, maxDih = double.MinValue, sumMinDih = 0;
            double minVol = double.MaxValue, maxVol = double.MinValue, sumVol = 0;
            int worstIdx = 0, bestIdx = 0, invertedCount = 0;

            for (int i = 0; i < n; ++i)
            {
                var q = perElementQualities[i];

                if (q.MeanRatio < minEta) { minEta = q.MeanRatio; worstIdx = i; }
                if (q.MeanRatio > maxEta) { maxEta = q.MeanRatio; bestIdx  = i; }
                sumEta += q.MeanRatio;

                if (q.MinDihedralAngleDegrees < minDih) minDih = q.MinDihedralAngleDegrees;
                if (q.MaxDihedralAngleDegrees > maxDih) maxDih = q.MaxDihedralAngleDegrees;
                sumMinDih += q.MinDihedralAngleDegrees;

                double absVol = Math.Abs(q.Volume);
                if (absVol < minVol) minVol = absVol;
                if (absVol > maxVol) maxVol = absVol;
                sumVol += q.Volume;

                if (q.IsInverted) ++invertedCount;
            }

            double avgEta = sumEta / n;

            // Standard deviation of mean ratio
            double sumSq = 0;
            foreach (var q in perElementQualities)
                sumSq += (q.MeanRatio - avgEta) * (q.MeanRatio - avgEta);
            double stdEta = Math.Sqrt(sumSq / n);

            return new MeshQualityStatistics(
                minEta, maxEta, avgEta, stdEta,
                worstIdx, bestIdx,
                minDih, maxDih, sumMinDih / n,
                minVol, maxVol, sumVol,
                n, invertedCount);
        }

        /// <summary>
        /// Convenience overload that discards the per-element array.
        /// </summary>
        public MeshQualityStatistics ComputeQualityStatistics()
            => ComputeQualityStatistics(out _);

        // ---------------------------------------------------------------
        // Laplacian smoothing
        // ---------------------------------------------------------------

        /// <summary>
        /// Return a new <see cref="TetrahedralMesh"/> whose interior vertices
        /// have been smoothed using iterative Laplacian (centroid) relaxation.
        ///
        /// <para><b>Algorithm</b> – for each iteration:</para>
        /// <list type="number">
        ///   <item><description>
        ///     For every <b>interior</b> vertex v (i.e. not on the outer surface),
        ///     compute the centroid C of all vertices that share at least one
        ///     tetrahedron with v.
        ///   </description></item>
        ///   <item><description>
        ///     Move v towards C by the relaxation factor λ:
        ///     <c>v_new = v + λ × (C − v)</c>
        ///   </description></item>
        /// </list>
        ///
        /// <para>Boundary vertices are <b>never moved</b>, so the outer geometry
        /// is preserved exactly.</para>
        /// </summary>
        /// <param name="iterations">
        ///   Number of smoothing passes.  Must be ≥ 0.
        ///   0 returns a copy of this mesh unchanged.
        /// </param>
        /// <param name="factor">
        ///   Relaxation factor λ ∈ (0, 1].
        ///   1.0 = move fully to the centroid (fastest, may shrink convex regions).
        ///   0.5 = move halfway – balanced default.
        /// </param>
        /// <returns>New <see cref="TetrahedralMesh"/> with smoothed vertex positions.</returns>
        public TetrahedralMesh CreateSmoothed(int iterations, double factor = 0.5)
        {
            if (NodesPerElement != 4)
                throw new InvalidOperationException(
                    "Laplacian smoothing is only supported for TET4 (linear) elements. " +
                    "Second-order (TET10) meshes cannot be smoothed with this method.");
            if (iterations < 0)  throw new ArgumentOutOfRangeException(nameof(iterations));
            if (factor <= 0 || factor > 1.0)
                throw new ArgumentOutOfRangeException(nameof(factor),
                    "factor must be in the range (0, 1].");

            if (iterations == 0)
                return new TetrahedralMesh((double[])Vertices.Clone(), Tetrahedra, NodesPerElement);

            int nv = VertexCount;
            int ne = TetCount;

            // ------------------------------------------------------------------
            // 1. Build per-vertex adjacency (all vertices sharing a tet)
            // ------------------------------------------------------------------
            // Use List<int> per vertex – most vertices have O(10–30) neighbours.
            var neighbours = new List<int>[nv];
            for (int v = 0; v < nv; ++v)
                neighbours[v] = new List<int>(16);

            for (int t = 0; t < ne; ++t)
            {
                int bi = t * 4;
                int a = Tetrahedra[bi],
                    b = Tetrahedra[bi + 1],
                    c = Tetrahedra[bi + 2],
                    d = Tetrahedra[bi + 3];

                AddNeighbour(neighbours[a], b);
                AddNeighbour(neighbours[a], c);
                AddNeighbour(neighbours[a], d);
                AddNeighbour(neighbours[b], a);
                AddNeighbour(neighbours[b], c);
                AddNeighbour(neighbours[b], d);
                AddNeighbour(neighbours[c], a);
                AddNeighbour(neighbours[c], b);
                AddNeighbour(neighbours[c], d);
                AddNeighbour(neighbours[d], a);
                AddNeighbour(neighbours[d], b);
                AddNeighbour(neighbours[d], c);
            }

            // ------------------------------------------------------------------
            // 2. Identify boundary (surface) vertices
            //    A face shared by exactly 1 tet is a boundary face.
            // ------------------------------------------------------------------
            var faceCounter = new Dictionary<(int, int, int), int>(ne * 4);

            for (int t = 0; t < ne; ++t)
            {
                int bi = t * 4;
                int a = Tetrahedra[bi],
                    b = Tetrahedra[bi + 1],
                    c = Tetrahedra[bi + 2],
                    d = Tetrahedra[bi + 3];

                CountFace(faceCounter, a, c, b);
                CountFace(faceCounter, a, b, d);
                CountFace(faceCounter, b, c, d);
                CountFace(faceCounter, a, d, c);
            }

            var isBoundary = new bool[nv];
            foreach (var kv in faceCounter)
            {
                if (kv.Value == 1)
                {
                    isBoundary[kv.Key.Item1] = true;
                    isBoundary[kv.Key.Item2] = true;
                    isBoundary[kv.Key.Item3] = true;
                }
            }

            // ------------------------------------------------------------------
            // 3. Iterative Laplacian relaxation
            // ------------------------------------------------------------------
            double[] cur  = (double[])Vertices.Clone();
            double[] next = new double[cur.Length];

            for (int iter = 0; iter < iterations; ++iter)
            {
                Array.Copy(cur, next, cur.Length);

                for (int v = 0; v < nv; ++v)
                {
                    if (isBoundary[v]) continue;

                    var nbrs = neighbours[v];
                    if (nbrs.Count == 0) continue;

                    // Compute centroid of neighbour positions in 'cur'
                    double cx = 0, cy = 0, cz = 0;
                    foreach (int n in nbrs)
                    {
                        cx += cur[n * 3];
                        cy += cur[n * 3 + 1];
                        cz += cur[n * 3 + 2];
                    }
                    double invN = 1.0 / nbrs.Count;
                    cx *= invN;
                    cy *= invN;
                    cz *= invN;

                    // Move v towards centroid by factor λ
                    next[v * 3]     = cur[v * 3]     + factor * (cx - cur[v * 3]);
                    next[v * 3 + 1] = cur[v * 3 + 1] + factor * (cy - cur[v * 3 + 1]);
                    next[v * 3 + 2] = cur[v * 3 + 2] + factor * (cz - cur[v * 3 + 2]);
                }

                // Swap buffers
                var tmp = cur;
                cur  = next;
                next = tmp;
            }

            return new TetrahedralMesh(cur, Tetrahedra, NodesPerElement);
        }

        // ---------------------------------------------------------------
        // Surface mesh extraction
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a Rhino <see cref="Mesh"/> containing only the exposed surface
        /// triangles of this tetrahedral mesh.
        /// </summary>
        public Mesh ToRhinoSurfaceMesh()
        {
            var faceCount = new Dictionary<(int, int, int), int>(TetCount * 4);

            for (int t = 0; t < TetCount; ++t)
            {
                int i = t * 4;
                int a = Tetrahedra[i],
                    b = Tetrahedra[i + 1],
                    c = Tetrahedra[i + 2],
                    d = Tetrahedra[i + 3];

                CountFace(faceCount, a, c, b);
                CountFace(faceCount, a, b, d);
                CountFace(faceCount, b, c, d);
                CountFace(faceCount, a, d, c);
            }

            var rhinoMesh = new Mesh();

            for (int v = 0; v < VertexCount; ++v)
                rhinoMesh.Vertices.Add(Vertices[v * 3], Vertices[v * 3 + 1], Vertices[v * 3 + 2]);

            foreach (var kv in faceCount)
            {
                if (kv.Value == 1)
                {
                    var (a, b, c) = kv.Key;
                    rhinoMesh.Faces.AddFace(a, b, c);
                }
            }

            rhinoMesh.Normals.ComputeNormals();
            rhinoMesh.Compact();
            return rhinoMesh;
        }

        // ---------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------

        /// Add n to list only if it is not already present (small lists ⇒ linear scan is fine).
        private static void AddNeighbour(List<int> list, int n)
        {
            if (!list.Contains(n))
                list.Add(n);
        }

        private static void CountFace(Dictionary<(int, int, int), int> dict,
                                       int a, int b, int c)
        {
            // Canonical sorted key – same triangle regardless of winding.
            int x = a, y = b, z = c;
            if (x > y) { int t = x; x = y; y = t; }
            if (y > z) { int t = y; y = z; z = t; }
            if (x > y) { int t = x; x = y; y = t; }
            var key = (x, y, z);
            dict.TryGetValue(key, out int count);
            dict[key] = count + 1;
        }
    }
}

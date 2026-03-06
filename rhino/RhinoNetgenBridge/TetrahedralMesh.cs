using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// Stores the result of a tetrahedral meshing operation.
    ///
    /// Vertices are stored in a flat array [x0,y0,z0, x1,y1,z1, …] and
    /// tetrahedra as a flat array of 0-based vertex indices [a,b,c,d, …].
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
        /// Flat array of tetrahedral element indices (0-based).
        /// Length = <see cref="TetCount"/> * 4.
        /// Layout: [a0,b0,c0,d0, a1,b1,c1,d1, …]
        /// </summary>
        public int[] Tetrahedra { get; }

        /// <summary>Number of vertices in the mesh.</summary>
        public int VertexCount => Vertices.Length / 3;

        /// <summary>Number of tetrahedral elements in the mesh.</summary>
        public int TetCount => Tetrahedra.Length / 4;

        internal TetrahedralMesh(double[] vertices, int[] tetrahedra)
        {
            Vertices   = vertices   ?? throw new ArgumentNullException(nameof(vertices));
            Tetrahedra = tetrahedra ?? throw new ArgumentNullException(nameof(tetrahedra));
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
        /// Return the four 0-based vertex indices of tetrahedron
        /// <paramref name="index"/> as a value tuple.
        /// </summary>
        public (int A, int B, int C, int D) GetTetrahedron(int index)
        {
            if (index < 0 || index >= TetCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            int i = index * 4;
            return (Tetrahedra[i], Tetrahedra[i + 1], Tetrahedra[i + 2], Tetrahedra[i + 3]);
        }

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
            if (iterations < 0)  throw new ArgumentOutOfRangeException(nameof(iterations));
            if (factor <= 0 || factor > 1.0)
                throw new ArgumentOutOfRangeException(nameof(factor),
                    "factor must be in the range (0, 1].");

            if (iterations == 0)
                return new TetrahedralMesh((double[])Vertices.Clone(), Tetrahedra);

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

            return new TetrahedralMesh(cur, Tetrahedra);
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

using System;
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
            Vertices    = vertices ?? throw new ArgumentNullException(nameof(vertices));
            Tetrahedra  = tetrahedra ?? throw new ArgumentNullException(nameof(tetrahedra));
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

        /// <summary>
        /// Build a Rhino <see cref="Mesh"/> containing only the exposed surface
        /// triangles of this tetrahedral mesh.
        ///
        /// This is useful for visual inspection inside Rhino without requiring
        /// a dedicated volumetric renderer.  The surface is extracted by
        /// collecting faces whose opposite face is not shared by another
        /// tetrahedron.
        ///
        /// <para>
        /// Note: For large meshes the O(n²) boundary-detection is replaced by
        /// a hash-set approach for efficiency.
        /// </para>
        /// </summary>
        public Mesh ToRhinoSurfaceMesh()
        {
            // Collect all oriented faces as (sorted triple → count) map.
            // A boundary face appears exactly once.
            var faceCount = new System.Collections.Generic.Dictionary<(int, int, int), int>(
                TetCount * 4);

            for (int t = 0; t < TetCount; ++t)
            {
                int i = t * 4;
                int a = Tetrahedra[i];
                int b = Tetrahedra[i + 1];
                int c = Tetrahedra[i + 2];
                int d = Tetrahedra[i + 3];

                // The four faces of a tetrahedron (consistent outward orientation).
                AddFace(faceCount, a, c, b);
                AddFace(faceCount, a, b, d);
                AddFace(faceCount, b, c, d);
                AddFace(faceCount, a, d, c);
            }

            var rhinoMesh = new Mesh();

            // Copy all vertices
            for (int v = 0; v < VertexCount; ++v)
                rhinoMesh.Vertices.Add(Vertices[v * 3], Vertices[v * 3 + 1], Vertices[v * 3 + 2]);

            // Add only boundary faces (count == 1)
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

        private static void AddFace(
            System.Collections.Generic.Dictionary<(int, int, int), int> dict,
            int a, int b, int c)
        {
            // Canonical key: sort indices so that the same triangle with any
            // winding is detected as the same face.
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

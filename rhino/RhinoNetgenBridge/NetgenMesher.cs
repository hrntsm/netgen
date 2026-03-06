using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// High-level entry point for generating tetrahedral meshes from Rhino geometry.
    ///
    /// <para>
    /// <b>Typical usage inside a Rhino command or GH component:</b>
    /// <code>
    ///   // Call once per application lifecycle (idempotent after the first call)
    ///   NetgenMesher.Initialize();
    ///
    ///   var mp = MeshingParameters.Medium();
    ///   TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp);
    ///   if (tet == null)
    ///       RhinoApp.WriteLine("Meshing failed.");
    ///   else
    ///       RhinoApp.WriteLine($"Generated {tet.TetCount} tets, {tet.VertexCount} vertices.");
    ///
    ///   // Optionally display the surface of the tet mesh in Rhino
    ///   Mesh surfMesh = tet.ToRhinoSurfaceMesh();
    ///   doc.Objects.AddMesh(surfMesh);
    /// </code>
    /// </para>
    ///
    /// <para>
    /// <b>Threading:</b> netgen is not thread-safe internally.  Do not call
    /// <see cref="GenerateFromBrep"/> or <see cref="GenerateFromMesh"/> from
    /// multiple threads simultaneously.
    /// </para>
    /// </summary>
    public static class NetgenMesher
    {
        private static bool _initialised;
        private static readonly object _initLock = new object();

        // ---------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------

        /// <summary>
        /// Initialise the underlying netgen kernel.
        ///
        /// Safe to call multiple times; only the first call has any effect.
        /// You must call this before any meshing method.
        /// </summary>
        public static void Initialize()
        {
            lock (_initLock)
            {
                if (_initialised) return;
                NetgenNative.NGW_Init();
                _initialised = true;
            }
        }

        /// <summary>
        /// Shut down the netgen kernel cleanly.
        ///
        /// Call this when the host application is closing.  After calling
        /// Shutdown() you must not call any other method without calling
        /// <see cref="Initialize"/> again.
        /// </summary>
        public static void Shutdown()
        {
            lock (_initLock)
            {
                if (!_initialised) return;
                NetgenNative.NGW_Exit();
                _initialised = false;
            }
        }

        // ---------------------------------------------------------------
        // Meshing from Rhino Brep
        // ---------------------------------------------------------------

        /// <summary>
        /// Generate a tetrahedral mesh from a Rhino <see cref="Brep"/>.
        ///
        /// The Brep must represent a closed solid.  Internally, the Brep is
        /// first tessellated into a triangulated surface mesh using
        /// <paramref name="meshingParams"/> (or default medium settings if
        /// <c>null</c>) and then handed to netgen for volume meshing.
        /// </summary>
        /// <param name="brep">Closed solid Brep to mesh.</param>
        /// <param name="meshingParams">
        ///   Netgen meshing parameters, or <c>null</c> for medium defaults.
        /// </param>
        /// <param name="rhinoMeshParams">
        ///   Rhino surface meshing parameters used to tessellate the Brep faces
        ///   before passing to netgen.  Pass <c>null</c> for Rhino's default
        ///   smooth settings.
        /// </param>
        /// <returns>
        ///   A <see cref="TetrahedralMesh"/>, or <c>null</c> if meshing failed.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///   Thrown when <paramref name="brep"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///   Thrown when <see cref="Initialize"/> has not been called.
        /// </exception>
        public static TetrahedralMesh GenerateFromBrep(
            Brep brep,
            MeshingParameters meshingParams  = null,
            Rhino.Geometry.MeshingParameters rhinoMeshParams = null)
        {
            if (brep == null) throw new ArgumentNullException(nameof(brep));
            EnsureInitialised();

            // ------------------------------------------------------------------
            // 1. Tessellate the Brep into a unified triangulated surface mesh
            // ------------------------------------------------------------------
            Mesh surfaceMesh = TessellateBrep(brep, rhinoMeshParams);
            if (surfaceMesh == null || surfaceMesh.Faces.Count == 0)
                return null;

            return GenerateFromMesh(surfaceMesh, meshingParams);
        }

        // ---------------------------------------------------------------
        // Meshing from a Rhino Mesh (surface mesh)
        // ---------------------------------------------------------------

        /// <summary>
        /// Generate a tetrahedral mesh from a closed triangulated Rhino
        /// <see cref="Mesh"/>.
        ///
        /// The input mesh <b>must</b> be a closed, manifold surface.
        /// Any quads are automatically triangulated before being passed to
        /// netgen.
        /// </summary>
        /// <param name="surfaceMesh">Closed triangulated surface mesh.</param>
        /// <param name="meshingParams">
        ///   Netgen meshing parameters, or <c>null</c> for medium defaults.
        /// </param>
        /// <returns>
        ///   A <see cref="TetrahedralMesh"/>, or <c>null</c> if meshing failed.
        /// </returns>
        public static TetrahedralMesh GenerateFromMesh(
            Mesh surfaceMesh,
            MeshingParameters meshingParams = null)
        {
            if (surfaceMesh == null) throw new ArgumentNullException(nameof(surfaceMesh));
            EnsureInitialised();

            meshingParams ??= MeshingParameters.Medium();

            // ------------------------------------------------------------------
            // 2. Triangulate all faces (quads → 2 tris)
            // ------------------------------------------------------------------
            Mesh triMesh = surfaceMesh.DuplicateMesh();
            triMesh.Faces.ConvertQuadsToTriangles();
            triMesh.Vertices.CombineIdentical(true, true);
            triMesh.Weld(Math.PI);

            // ------------------------------------------------------------------
            // 3. Extract flat vertex and index arrays
            // ------------------------------------------------------------------
            int nv = triMesh.Vertices.Count;
            double[] vertices = new double[nv * 3];
            for (int i = 0; i < nv; ++i)
            {
                var pt = triMesh.Vertices[i];
                vertices[i * 3 + 0] = pt.X;
                vertices[i * 3 + 1] = pt.Y;
                vertices[i * 3 + 2] = pt.Z;
            }

            int nf = triMesh.Faces.Count;
            int[] triangles = new int[nf * 3];
            for (int i = 0; i < nf; ++i)
            {
                var face = triMesh.Faces[i];
                triangles[i * 3 + 0] = face.A;
                triangles[i * 3 + 1] = face.B;
                triangles[i * 3 + 2] = face.C;
            }

            // ------------------------------------------------------------------
            // 4. Call netgen via P/Invoke
            // ------------------------------------------------------------------
            IntPtr resultHandle = NetgenNative.NGW_GenerateTetrahedralMesh(
                nv, vertices,
                nf, triangles,
                meshingParams.MaxElementSize,
                meshingParams.Fineness,
                meshingParams.Grading);

            if (resultHandle == IntPtr.Zero)
                return null;

            try
            {
                int outNV = NetgenNative.NGW_GetNumPoints(resultHandle);
                int outNE = NetgenNative.NGW_GetNumTets(resultHandle);

                if (outNV == 0 || outNE == 0)
                    return null;

                double[] outVertices = new double[outNV * 3];
                int[]    outTets     = new int[outNE * 4];

                NetgenNative.NGW_GetPoints(resultHandle, outVertices);
                NetgenNative.NGW_GetTets(resultHandle, outTets);

                return new TetrahedralMesh(outVertices, outTets);
            }
            finally
            {
                NetgenNative.NGW_FreeResult(resultHandle);
            }
        }

        // ---------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------

        private static void EnsureInitialised()
        {
            if (!_initialised)
                throw new InvalidOperationException(
                    $"Call {nameof(NetgenMesher)}.{nameof(Initialize)}() before meshing.");
        }

        /// <summary>
        /// Tessellate all faces of a Brep into a single welded triangle mesh.
        /// </summary>
        private static Mesh TessellateBrep(
            Brep brep,
            Rhino.Geometry.MeshingParameters rhinoMeshParams)
        {
            var mp = rhinoMeshParams
                ?? Rhino.Geometry.MeshingParameters.Smooth;

            Mesh[] faceMeshes = Mesh.CreateFromBrep(brep, mp);
            if (faceMeshes == null || faceMeshes.Length == 0)
                return null;

            // Join all per-face meshes into a single mesh
            var combined = new Mesh();
            foreach (var m in faceMeshes)
                combined.Append(m);

            combined.Faces.ConvertQuadsToTriangles();
            combined.Vertices.CombineIdentical(true, true);
            combined.Weld(Math.PI);
            combined.Compact();

            return combined;
        }
    }
}

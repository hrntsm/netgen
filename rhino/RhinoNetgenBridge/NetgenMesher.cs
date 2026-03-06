using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// High-level entry point for generating tetrahedral meshes from Rhino geometry.
    ///
    /// <para><b>Typical usage inside a Rhino command or GH component:</b></para>
    /// <code>
    ///   NetgenMesher.Initialize();  // once per application
    ///
    ///   var mp = MeshingParameters.Fine();
    ///   mp.ElementsPerEdge  = 4;    // finer along geometry edges
    ///   mp.ElementsPerCurve = 4;    // finer on curved surfaces
    ///
    ///   // Optional local refinement
    ///   var ptRestrictions = new[]
    ///   {
    ///       new PointSizeRestriction(new Point3d(10, 0, 5), maxElementSize: 0.5),
    ///   };
    ///   var boxRestrictions = new[]
    ///   {
    ///       new BoxSizeRestriction(new Point3d(-5,-5,0), new Point3d(5,5,3), 0.8),
    ///   };
    ///
    ///   TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(
    ///       brep, mp,
    ///       pointRestrictions: ptRestrictions,
    ///       boxRestrictions:   boxRestrictions);
    ///
    ///   RhinoApp.WriteLine($"Vertices: {tet.VertexCount}  Tets: {tet.TetCount}");
    ///   doc.Objects.AddMesh(tet.ToRhinoSurfaceMesh());
    /// </code>
    ///
    /// <para><b>Threading:</b> netgen is not thread-safe internally.  Do not
    /// call <see cref="GenerateFromBrep"/> or <see cref="GenerateFromMesh"/>
    /// from multiple threads simultaneously.</para>
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
        /// Safe to call multiple times; only the first call has any effect.
        /// Must be called before any meshing method.
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
        /// After calling Shutdown() you must not call any meshing method
        /// without calling <see cref="Initialize"/> again.
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
        /// Generate a tetrahedral mesh from a closed Rhino <see cref="Brep"/>.
        ///
        /// Internally the Brep is first tessellated into a triangulated surface
        /// mesh and then passed to netgen for volume meshing.
        /// </summary>
        /// <param name="brep">Closed solid Brep to mesh.</param>
        /// <param name="meshingParams">
        ///   Netgen meshing parameters, or <c>null</c> for medium defaults.
        /// </param>
        /// <param name="rhinoMeshParams">
        ///   Rhino surface-tessellation parameters.  Pass <c>null</c> for
        ///   Rhino's default smooth settings.
        /// </param>
        /// <param name="pointRestrictions">
        ///   Optional point-based local size restrictions.
        ///   Each entry restricts the element size at and near a given point.
        /// </param>
        /// <param name="boxRestrictions">
        ///   Optional box-based local size restrictions.
        ///   Each entry restricts the element size inside an axis-aligned box.
        /// </param>
        /// <returns>
        ///   A <see cref="TetrahedralMesh"/>, or <c>null</c> if meshing failed.
        /// </returns>
        public static TetrahedralMesh GenerateFromBrep(
            Brep brep,
            MeshingParameters meshingParams = null,
            Rhino.Geometry.MeshingParameters rhinoMeshParams = null,
            IReadOnlyList<PointSizeRestriction> pointRestrictions = null,
            IReadOnlyList<BoxSizeRestriction>   boxRestrictions   = null)
        {
            if (brep == null) throw new ArgumentNullException(nameof(brep));
            EnsureInitialised();

            Mesh surfaceMesh = TessellateBrep(brep, rhinoMeshParams);
            if (surfaceMesh == null || surfaceMesh.Faces.Count == 0)
                return null;

            return GenerateFromMesh(surfaceMesh, meshingParams,
                                    pointRestrictions, boxRestrictions);
        }

        // ---------------------------------------------------------------
        // Meshing from a Rhino Mesh (surface mesh)
        // ---------------------------------------------------------------

        /// <summary>
        /// Generate a tetrahedral mesh from a closed triangulated Rhino
        /// <see cref="Mesh"/>.
        ///
        /// The input mesh must be a closed, manifold surface.
        /// Quads are automatically triangulated before being passed to netgen.
        /// </summary>
        /// <param name="surfaceMesh">Closed triangulated surface mesh.</param>
        /// <param name="meshingParams">
        ///   Netgen meshing parameters, or <c>null</c> for medium defaults.
        /// </param>
        /// <param name="pointRestrictions">
        ///   Optional point-based local size restrictions.
        /// </param>
        /// <param name="boxRestrictions">
        ///   Optional box-based local size restrictions.
        /// </param>
        /// <returns>
        ///   A <see cref="TetrahedralMesh"/>, or <c>null</c> if meshing failed.
        /// </returns>
        public static TetrahedralMesh GenerateFromMesh(
            Mesh surfaceMesh,
            MeshingParameters meshingParams = null,
            IReadOnlyList<PointSizeRestriction> pointRestrictions = null,
            IReadOnlyList<BoxSizeRestriction>   boxRestrictions   = null)
        {
            if (surfaceMesh == null) throw new ArgumentNullException(nameof(surfaceMesh));
            EnsureInitialised();

            meshingParams ??= MeshingParameters.Medium();

            // ------------------------------------------------------------------
            // 1. Triangulate all faces (quads → 2 tris) and weld vertices
            // ------------------------------------------------------------------
            Mesh triMesh = surfaceMesh.DuplicateMesh();
            triMesh.Faces.ConvertQuadsToTriangles();
            triMesh.Vertices.CombineIdentical(true, true);
            triMesh.Weld(Math.PI);

            // ------------------------------------------------------------------
            // 2. Extract flat vertex and index arrays
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
            // 3. Build native meshing-params struct from MeshingParameters
            // ------------------------------------------------------------------
            var nmp = new NetgenNative.NativeMeshingParams
            {
                MaxH             = meshingParams.MaxElementSize,
                MinH             = meshingParams.MinElementSize,
                Fineness         = meshingParams.Fineness,
                Grading          = meshingParams.Grading,
                ElementsPerEdge  = meshingParams.ElementsPerEdge,
                ElementsPerCurve = meshingParams.ElementsPerCurve,
                CloseEdgeFact    = meshingParams.CloseEdgeFactor,
                MinEdgeLen       = meshingParams.MinEdgeLength,
                CloseEdgeEnable  = meshingParams.CloseEdgeRefinement ? 1 : 0,
                MinEdgeLenEnable = meshingParams.EnforceMinEdgeLength ? 1 : 0,
                OptSteps2D       = meshingParams.OptimizationSteps2D,
                OptSteps3D       = meshingParams.OptimizationSteps3D,
            };

            // ------------------------------------------------------------------
            // 4. Build restriction arrays (null when empty)
            // ------------------------------------------------------------------
            var ptArr  = BuildPointArray(pointRestrictions);
            var boxArr = BuildBoxArray(boxRestrictions);

            int npt  = ptArr  != null ? ptArr.Length  : 0;
            int nbox = boxArr != null ? boxArr.Length  : 0;

            // ------------------------------------------------------------------
            // 5. Call netgen via P/Invoke
            // ------------------------------------------------------------------
            IntPtr resultHandle = (npt > 0 || nbox > 0)
                ? NetgenNative.NGW_GenerateTetrahedralMeshEx(
                    nv, vertices, nf, triangles,
                    ref nmp,
                    npt,  ptArr,
                    nbox, boxArr)
                : NetgenNative.NGW_GenerateTetrahedralMesh(
                    nv, vertices, nf, triangles,
                    ref nmp);

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

        private static Mesh TessellateBrep(
            Brep brep,
            Rhino.Geometry.MeshingParameters rhinoMeshParams)
        {
            var mp = rhinoMeshParams ?? Rhino.Geometry.MeshingParameters.Smooth;
            Mesh[] faceMeshes = Mesh.CreateFromBrep(brep, mp);
            if (faceMeshes == null || faceMeshes.Length == 0)
                return null;

            var combined = new Mesh();
            foreach (var m in faceMeshes)
                combined.Append(m);

            combined.Faces.ConvertQuadsToTriangles();
            combined.Vertices.CombineIdentical(true, true);
            combined.Weld(Math.PI);
            combined.Compact();
            return combined;
        }

        private static NetgenNative.NativePointRestriction[] BuildPointArray(
            IReadOnlyList<PointSizeRestriction> list)
        {
            if (list == null || list.Count == 0) return null;
            var arr = new NetgenNative.NativePointRestriction[list.Count];
            for (int i = 0; i < list.Count; ++i)
                arr[i] = new NetgenNative.NativePointRestriction
                {
                    X = list[i].Point.X,
                    Y = list[i].Point.Y,
                    Z = list[i].Point.Z,
                    H = list[i].MaxElementSize,
                };
            return arr;
        }

        private static NetgenNative.NativeBoxRestriction[] BuildBoxArray(
            IReadOnlyList<BoxSizeRestriction> list)
        {
            if (list == null || list.Count == 0) return null;
            var arr = new NetgenNative.NativeBoxRestriction[list.Count];
            for (int i = 0; i < list.Count; ++i)
            {
                var b = list[i].Box;
                arr[i] = new NetgenNative.NativeBoxRestriction
                {
                    XMin = b.Min.X, YMin = b.Min.Y, ZMin = b.Min.Z,
                    XMax = b.Max.X, YMax = b.Max.Y, ZMax = b.Max.Z,
                    H    = list[i].MaxElementSize,
                };
            }
            return arr;
        }
    }
}

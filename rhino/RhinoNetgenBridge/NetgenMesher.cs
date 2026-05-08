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
    ///   mp.ElementsPerEdge  = 4;
    ///   mp.ElementsPerCurve = 4;
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
    ///   // Optional progress feedback
    ///   Action&lt;string,int&gt; progress = (stage, pct) =>
    ///       RhinoApp.WriteLine($"[{pct}%] {stage}");
    ///
    ///   TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(
    ///       brep, mp,
    ///       pointRestrictions: ptRestrictions,
    ///       boxRestrictions:   boxRestrictions,
    ///       onProgress:        progress);
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

        // Per-thread storage for the native delegate so that the GC does not
        // collect it while the native call is in progress.
        [ThreadStatic]
        private static NetgenNative.ProgressCallbackDelegate? _nativeProgressDelegate;

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
        // Meshing from a single Rhino Brep
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
        /// </param>
        /// <param name="boxRestrictions">
        ///   Optional box-based local size restrictions.
        /// </param>
        /// <param name="onProgress">
        ///   Optional callback invoked at key meshing stages.
        ///   Receives a stage name ("edges", "surface", "volume", "done") and
        ///   an approximate completion percentage (0–100).
        /// </param>
        /// <returns>A <see cref="TetrahedralMesh"/> with the generated elements.</returns>
        /// <exception cref="NetgenMeshingException">
        ///   Thrown if the netgen kernel reports an error.
        /// </exception>
        public static TetrahedralMesh GenerateFromBrep(
            Brep brep,
            MeshingParameters? meshingParams = null,
            Rhino.Geometry.MeshingParameters? rhinoMeshParams = null,
            IReadOnlyList<PointSizeRestriction>? pointRestrictions = null,
            IReadOnlyList<BoxSizeRestriction>? boxRestrictions = null,
            Action<string, int>? onProgress = null)
        {
            if (brep == null) throw new ArgumentNullException(nameof(brep));
            EnsureInitialised();

            Mesh? surfaceMesh = TessellateBrep(brep, rhinoMeshParams);
            if (surfaceMesh == null || surfaceMesh.Faces.Count == 0)
                throw new NetgenMeshingException(NetgenErrorCode.InvalidInput,
                    "Failed to tessellate the Brep into a surface mesh. " +
                    "Ensure the Brep is a closed solid.");

            return GenerateFromMesh(surfaceMesh, meshingParams,
                                    pointRestrictions, boxRestrictions, onProgress);
        }

        // ---------------------------------------------------------------
        // Meshing from multiple Rhino Breps (assembly)
        // ---------------------------------------------------------------

        /// <summary>
        /// Generate a tetrahedral mesh from an assembly of closed Rhino
        /// <see cref="Brep"/> objects.
        ///
        /// <para>All Breps are tessellated, combined into a single closed
        /// surface mesh, and then passed to netgen for volume meshing.  The
        /// resulting tetrahedral mesh spans the union of all enclosed volumes.</para>
        ///
        /// <para>Best results are obtained when the Breps form a single
        /// watertight solid (e.g. a BooleanUnion result).  Overlapping Breps
        /// or open shells may cause meshing to fail.</para>
        /// </summary>
        /// <param name="breps">
        ///   One or more closed solid Breps.  Must not be null or empty.
        /// </param>
        /// <param name="meshingParams">
        ///   Netgen meshing parameters, or <c>null</c> for medium defaults.
        /// </param>
        /// <param name="rhinoMeshParams">
        ///   Rhino surface-tessellation parameters.  Pass <c>null</c> for
        ///   Rhino's default smooth settings.
        /// </param>
        /// <param name="pointRestrictions">
        ///   Optional point-based local size restrictions.
        /// </param>
        /// <param name="boxRestrictions">
        ///   Optional box-based local size restrictions.
        /// </param>
        /// <param name="onProgress">
        ///   Optional progress callback.
        /// </param>
        /// <returns>A <see cref="TetrahedralMesh"/> with the generated elements.</returns>
        /// <exception cref="NetgenMeshingException">
        ///   Thrown if the netgen kernel reports an error.
        /// </exception>
        public static TetrahedralMesh GenerateFromBreps(
            IReadOnlyList<Brep> breps,
            MeshingParameters? meshingParams = null,
            Rhino.Geometry.MeshingParameters? rhinoMeshParams = null,
            IReadOnlyList<PointSizeRestriction>? pointRestrictions = null,
            IReadOnlyList<BoxSizeRestriction>? boxRestrictions = null,
            Action<string, int>? onProgress = null)
        {
            if (breps == null || breps.Count == 0)
                throw new ArgumentException("At least one Brep is required.", nameof(breps));
            EnsureInitialised();

            var combined = new Mesh();
            var mp = rhinoMeshParams ?? Rhino.Geometry.MeshingParameters.QualityRenderMesh;

            for (int b = 0; b < breps.Count; ++b)
            {
                var brep = breps[b];
                if (brep == null)
                    throw new ArgumentException($"breps[{b}] is null.", nameof(breps));

                Mesh[] faceMeshes = Mesh.CreateFromBrep(brep, mp);
                if (faceMeshes == null || faceMeshes.Length == 0)
                    throw new NetgenMeshingException(NetgenErrorCode.InvalidInput,
                        $"Failed to tessellate Brep at index {b}. " +
                        "Ensure all Breps are closed solids.");

                foreach (var m in faceMeshes)
                    combined.Append(m);
            }

            combined.Faces.ConvertQuadsToTriangles();
            combined.Vertices.CombineIdentical(true, true);
            combined.Weld(Math.PI);
            combined.Compact();

            if (combined.Faces.Count == 0)
                throw new NetgenMeshingException(NetgenErrorCode.InvalidInput,
                    "Combined surface mesh is empty after tessellation.");

            return GenerateFromMesh(combined, meshingParams,
                                    pointRestrictions, boxRestrictions, onProgress);
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
        /// <param name="onProgress">
        ///   Optional progress callback.
        /// </param>
        /// <returns>A <see cref="TetrahedralMesh"/> with the generated elements.</returns>
        /// <exception cref="NetgenMeshingException">
        ///   Thrown if the netgen kernel reports an error.
        /// </exception>
        public static TetrahedralMesh GenerateFromMesh(
            Mesh surfaceMesh,
            MeshingParameters? meshingParams = null,
            IReadOnlyList<PointSizeRestriction>? pointRestrictions = null,
            IReadOnlyList<BoxSizeRestriction>? boxRestrictions = null,
            Action<string, int>? onProgress = null)
        {
            if (surfaceMesh == null) throw new ArgumentNullException(nameof(surfaceMesh));
            EnsureInitialised();

            var effectiveMeshingParams = meshingParams ?? MeshingParameters.Medium();

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
                MaxH = effectiveMeshingParams.MaxElementSize,
                MinH = effectiveMeshingParams.MinElementSize,
                Fineness = effectiveMeshingParams.Fineness,
                Grading = effectiveMeshingParams.Grading,
                ElementsPerEdge = effectiveMeshingParams.ElementsPerEdge,
                ElementsPerCurve = effectiveMeshingParams.ElementsPerCurve,
                CloseEdgeFact = effectiveMeshingParams.CloseEdgeFactor,
                MinEdgeLen = effectiveMeshingParams.MinEdgeLength,
                CloseEdgeEnable = effectiveMeshingParams.CloseEdgeRefinement ? 1 : 0,
                MinEdgeLenEnable = effectiveMeshingParams.EnforceMinEdgeLength ? 1 : 0,
                OptSteps2D = effectiveMeshingParams.OptimizationSteps2D,
                OptSteps3D = effectiveMeshingParams.OptimizationSteps3D,
                OptSurfMeshEnable = effectiveMeshingParams.EnableSurfaceOptimization ? 1 : 0,
                OptVolMeshEnable = effectiveMeshingParams.EnableVolumeOptimization ? 1 : 0,
                SecondOrder = effectiveMeshingParams.SecondOrder ? 1 : 0,
                UniformRefSteps = effectiveMeshingParams.UniformRefinementSteps,
            };

            // ------------------------------------------------------------------
            // 4. Register progress callback
            // ------------------------------------------------------------------
            if (onProgress != null)
            {
                _nativeProgressDelegate = (stage, pct) => onProgress(stage, pct);
                NetgenNative.NGW_SetProgressCallback(_nativeProgressDelegate);
            }

            // ------------------------------------------------------------------
            // 5. Build restriction arrays (null when empty)
            // ------------------------------------------------------------------
            var ptArr = BuildPointArray(pointRestrictions);
            var boxArr = BuildBoxArray(boxRestrictions);

            int npt = ptArr != null ? ptArr.Length : 0;
            int nbox = boxArr != null ? boxArr.Length : 0;

            // ------------------------------------------------------------------
            // 6. Call netgen via P/Invoke
            // ------------------------------------------------------------------
            IntPtr resultHandle;
            try
            {
                resultHandle = (npt > 0 || nbox > 0)
                    ? NetgenNative.NGW_GenerateTetrahedralMeshEx(
                        nv, vertices, nf, triangles,
                        ref nmp,
                        npt, ptArr,
                        nbox, boxArr)
                    : NetgenNative.NGW_GenerateTetrahedralMesh(
                        nv, vertices, nf, triangles,
                        ref nmp);
            }
            finally
            {
                if (onProgress != null)
                {
                    NetgenNative.NGW_ClearProgressCallback();
                    _nativeProgressDelegate = null;
                }
            }

            if (resultHandle == IntPtr.Zero)
            {
                int rawCode = NetgenNative.NGW_GetLastError();
                var errorCode = (NetgenErrorCode)rawCode;
                throw new NetgenMeshingException(errorCode);
            }

            try
            {
                int outNV = NetgenNative.NGW_GetNumPoints(resultHandle);
                int outNE = NetgenNative.NGW_GetNumTets(resultHandle);
                int outNPE = NetgenNative.NGW_GetNodesPerElement(resultHandle);

                if (outNV == 0 || outNE == 0)
                    throw new NetgenMeshingException(NetgenErrorCode.VolumeMeshFailed,
                        "The netgen kernel returned an empty mesh.");

                double[] outVertices = new double[outNV * 3];
                int[] outTets = new int[outNE * outNPE];

                NetgenNative.NGW_GetPoints(resultHandle, outVertices);
                NetgenNative.NGW_GetTets(resultHandle, outTets);

                var result = new TetrahedralMesh(outVertices, outTets, outNPE);

                // Post-generation Laplacian smoothing (TET4 only)
                if (effectiveMeshingParams.LaplacianSmoothingIterations > 0
                    && result.NodesPerElement == 4)
                    result = result.CreateSmoothed(
                        effectiveMeshingParams.LaplacianSmoothingIterations,
                        effectiveMeshingParams.LaplacianSmoothingFactor);

                return result;
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

        private static Mesh? TessellateBrep(
            Brep brep,
            Rhino.Geometry.MeshingParameters? rhinoMeshParams)
        {
            var mp = rhinoMeshParams ?? Rhino.Geometry.MeshingParameters.QualityRenderMesh;
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

        private static NetgenNative.NativePointRestriction[]? BuildPointArray(
            IReadOnlyList<PointSizeRestriction>? list)
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

        private static NetgenNative.NativeBoxRestriction[]? BuildBoxArray(
            IReadOnlyList<BoxSizeRestriction>? list)
        {
            if (list == null || list.Count == 0) return null;
            var arr = new NetgenNative.NativeBoxRestriction[list.Count];
            for (int i = 0; i < list.Count; ++i)
            {
                var b = list[i].Box;
                arr[i] = new NetgenNative.NativeBoxRestriction
                {
                    XMin = b.Min.X,
                    YMin = b.Min.Y,
                    ZMin = b.Min.Z,
                    XMax = b.Max.X,
                    YMax = b.Max.Y,
                    ZMax = b.Max.Z,
                    H = list[i].MaxElementSize,
                };
            }
            return arr;
        }
    }
}

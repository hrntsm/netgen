using System;
using System.Runtime.InteropServices;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// Raw P/Invoke declarations for the netgen_wrapper native shared library.
    ///
    /// These methods map directly to the flat C API defined in netgen_wrapper.h.
    /// Application code should use <see cref="NetgenMesher"/> instead of calling
    /// these methods directly.
    /// </summary>
    internal static class NetgenNative
    {
        // Name of the native library without extension.
        // On Windows this resolves to netgen_wrapper.dll,
        // on Linux/macOS to libnetgen_wrapper.so / libnetgen_wrapper.dylib.
        private const string LibName = "netgen_wrapper";

        // ---------------------------------------------------------------
        // Library lifecycle
        // ---------------------------------------------------------------

        /// <summary>Initialise the netgen meshing kernel.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_Init();

        /// <summary>Shut down the netgen meshing kernel cleanly.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_Exit();

        // ---------------------------------------------------------------
        // Mesh generation
        // ---------------------------------------------------------------

        /// <summary>
        /// Generate a tetrahedral mesh from a closed triangulated surface.
        /// </summary>
        /// <param name="numVertices">Number of surface vertices.</param>
        /// <param name="vertices">
        ///   Flat array [x0,y0,z0, x1,y1,z1, …] of length numVertices*3.
        /// </param>
        /// <param name="numTriangles">Number of triangles.</param>
        /// <param name="triangles">
        ///   Flat array of 0-based triangle indices [i0,j0,k0, …]
        ///   of length numTriangles*3.
        /// </param>
        /// <param name="maxh">Maximum element size (use 1e6 for unconstrained).</param>
        /// <param name="fineness">Mesh density in [0,1].</param>
        /// <param name="grading">Grading in [0,1].</param>
        /// <returns>
        ///   Opaque handle to the result, or <see cref="IntPtr.Zero"/> on failure.
        ///   Must be released with <see cref="NGW_FreeResult"/>.
        /// </returns>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr NGW_GenerateTetrahedralMesh(
            int numVertices,
            [In] double[] vertices,
            int numTriangles,
            [In] int[] triangles,
            double maxh,
            double fineness,
            double grading);

        // ---------------------------------------------------------------
        // Result queries
        // ---------------------------------------------------------------

        /// <summary>Number of mesh vertices in the result.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NGW_GetNumPoints(IntPtr result);

        /// <summary>Number of tetrahedral elements in the result.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NGW_GetNumTets(IntPtr result);

        /// <summary>
        /// Copy vertex coordinates into a pre-allocated buffer of length
        /// <c>NGW_GetNumPoints(result) * 3</c>.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_GetPoints(IntPtr result, [Out] double[] outVertices);

        /// <summary>
        /// Copy tetrahedral indices (0-based) into a pre-allocated buffer of
        /// length <c>NGW_GetNumTets(result) * 4</c>.
        /// </summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_GetTets(IntPtr result, [Out] int[] outTets);

        /// <summary>Release a result handle. Passing <see cref="IntPtr.Zero"/> is safe.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_FreeResult(IntPtr result);
    }
}

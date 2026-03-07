using System;
using System.Runtime.InteropServices;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// Raw P/Invoke declarations for the netgen_wrapper native shared library.
    ///
    /// Application code should use <see cref="NetgenMesher"/> instead of
    /// calling these methods directly.
    /// </summary>
    internal static class NetgenNative
    {
        // On Windows resolves to netgen_wrapper.dll,
        // on Linux/macOS to libnetgen_wrapper.so / .dylib.
        private const string LibName = "netgen_wrapper";

        // ---------------------------------------------------------------
        // Progress callback delegate
        // ---------------------------------------------------------------

        /// <summary>
        /// Unmanaged function pointer type for the progress callback.
        /// Matches <c>NGW_ProgressCallback</c> in netgen_wrapper.h.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ProgressCallbackDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string stage,
            int percent);

        // ---------------------------------------------------------------
        // Blittable structs – must match netgen_wrapper.h exactly.
        // Fields are ordered (all doubles, then all ints) to avoid padding.
        // ---------------------------------------------------------------

        /// <summary>
        /// Mirror of <c>NGW_MeshingParams</c> in netgen_wrapper.h.
        /// Used for P/Invoke – keep field order and types in sync with the C struct.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeMeshingParams
        {
            // --- Element size ---
            public double MaxH;
            public double MinH;

            // --- Density / grading ---
            public double Fineness;
            public double Grading;

            // --- Curvature / edge resolution ---
            public double ElementsPerEdge;
            public double ElementsPerCurve;

            // --- Close-edge refinement ---
            public double CloseEdgeFact;

            // --- Minimum edge length ---
            public double MinEdgeLen;

            // --- Integer flags (grouped after all doubles) ---
            public int CloseEdgeEnable;
            public int MinEdgeLenEnable;
            public int OptSteps2D;
            public int OptSteps3D;
            public int OptSurfMeshEnable;
            public int OptVolMeshEnable;
            public int SecondOrder;       // 0=TET4, 1=TET10
            public int UniformRefSteps;   // uniform refinement passes
        }

        /// <summary>Mirror of <c>NGW_PointSizeRestriction</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePointRestriction
        {
            public double X;
            public double Y;
            public double Z;
            public double H;
        }

        /// <summary>Mirror of <c>NGW_BoxSizeRestriction</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeBoxRestriction
        {
            public double XMin;
            public double YMin;
            public double ZMin;
            public double XMax;
            public double YMax;
            public double ZMax;
            public double H;
        }

        // ---------------------------------------------------------------
        // Progress callback and error reporting
        // ---------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_SetProgressCallback(ProgressCallbackDelegate callback);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_ClearProgressCallback();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NGW_GetLastError();

        // ---------------------------------------------------------------
        // Library lifecycle
        // ---------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_Init();

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_Exit();

        // ---------------------------------------------------------------
        // Parameter helpers
        // ---------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_DefaultMeshingParams(ref NativeMeshingParams mp);

        // ---------------------------------------------------------------
        // Mesh generation
        // ---------------------------------------------------------------

        /// <summary>Simple generation without local size restrictions.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr NGW_GenerateTetrahedralMesh(
            int numVertices,
            [In] double[] vertices,
            int numTriangles,
            [In] int[] triangles,
            ref NativeMeshingParams mp);

        /// <summary>Generation with optional point- and box-based size restrictions.</summary>
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr NGW_GenerateTetrahedralMeshEx(
            int numVertices,
            [In] double[] vertices,
            int numTriangles,
            [In] int[] triangles,
            ref NativeMeshingParams mp,
            int numPointRestrictions,
            [In] NativePointRestriction[] pointRestrictions,
            int numBoxRestrictions,
            [In] NativeBoxRestriction[] boxRestrictions);

        // ---------------------------------------------------------------
        // Result queries
        // ---------------------------------------------------------------

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NGW_GetNumPoints(IntPtr result);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NGW_GetNumTets(IntPtr result);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int NGW_GetNodesPerElement(IntPtr result);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_GetPoints(IntPtr result, [Out] double[] outVertices);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_GetTets(IntPtr result, [Out] int[] outTets);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void NGW_FreeResult(IntPtr result);
    }
}

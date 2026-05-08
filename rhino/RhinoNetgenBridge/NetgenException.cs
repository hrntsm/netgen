using System;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// Error codes returned by the netgen wrapper on meshing failure.
    /// Mirrors <c>NGW_ErrorCode</c> in netgen_wrapper.h.
    /// </summary>
    public enum NetgenErrorCode
    {
        /// <summary>No error – meshing succeeded.</summary>
        Ok = 0,

        /// <summary>
        /// Invalid input – null pointer, empty mesh, or other bad arguments
        /// passed to the native library.
        /// </summary>
        InvalidInput = 1,

        /// <summary>
        /// STL geometry initialisation failed.
        /// The input surface mesh may be degenerate or non-manifold.
        /// </summary>
        StlInitFailed = 2,

        /// <summary>
        /// Edge generation failed.
        /// Netgen could not determine geometry edges from the STL input.
        /// </summary>
        EdgeGenerationFailed = 3,

        /// <summary>
        /// Surface mesh generation failed.
        /// The geometry may be too complex or the meshing parameters too
        /// restrictive for netgen to produce a valid surface mesh.
        /// </summary>
        SurfaceMeshFailed = 4,

        /// <summary>
        /// Volume (tetrahedral) mesh generation failed.
        /// The surface mesh may not be closed, or the parameters may need
        /// adjustment.
        /// </summary>
        VolumeMeshFailed = 5,
    }

    /// <summary>
    /// Exception thrown when the netgen meshing kernel reports an error.
    ///
    /// <para>Use <see cref="ErrorCode"/> to determine the stage at which
    /// meshing failed and adjust geometry or meshing parameters accordingly.</para>
    ///
    /// <para>Example:</para>
    /// <code>
    ///   try
    ///   {
    ///       TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp);
    ///   }
    ///   catch (NetgenMeshingException ex)
    ///   {
    ///       RhinoApp.WriteLine($"Meshing failed: {ex.ErrorCode} – {ex.Message}");
    ///   }
    /// </code>
    /// </summary>
    public sealed class NetgenMeshingException : Exception
    {
        /// <summary>The specific stage at which meshing failed.</summary>
        public NetgenErrorCode ErrorCode { get; }

        /// <summary>
        /// Initialise a new <see cref="NetgenMeshingException"/> with the
        /// given error code and a default message.
        /// </summary>
        public NetgenMeshingException(NetgenErrorCode errorCode)
            : base(GetDefaultMessage(errorCode))
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initialise a new <see cref="NetgenMeshingException"/> with the
        /// given error code and a custom message.
        /// </summary>
        public NetgenMeshingException(NetgenErrorCode errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initialise a new <see cref="NetgenMeshingException"/> with the
        /// given error code, a custom message, and an inner exception.
        /// </summary>
        public NetgenMeshingException(NetgenErrorCode errorCode, string message,
                                      Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        private static string GetDefaultMessage(NetgenErrorCode code) =>
            code switch
            {
                NetgenErrorCode.Ok => "Meshing succeeded.",
                NetgenErrorCode.InvalidInput => "Invalid input: null pointer, empty mesh, or bad arguments.",
                NetgenErrorCode.StlInitFailed => "STL geometry initialisation failed. The surface mesh may be degenerate or non-manifold.",
                NetgenErrorCode.EdgeGenerationFailed => "Edge generation failed. Check that the surface mesh is valid and closed.",
                NetgenErrorCode.SurfaceMeshFailed => "Surface mesh generation failed. Adjust meshing parameters or simplify the geometry.",
                NetgenErrorCode.VolumeMeshFailed => "Volume mesh generation failed. Ensure the surface mesh is closed and try different parameters.",
                _ => $"Netgen meshing error (code {(int)code}).",
            };
    }
}

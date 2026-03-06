/*!
 * \file netgen_wrapper.h
 * \brief Flat C API wrapper around netgen's STL meshing for P/Invoke from C#
 *
 * This header exposes a plain C interface suitable for P/Invoke so that
 * a .NET application (e.g. a Rhino plugin) can generate tetrahedral meshes
 * from triangulated surface data without depending on C++ ABI details.
 *
 * Workflow:
 *   1. Call NGW_Init() once at application start.
 *   2. Call NGW_GenerateTetrahedralMesh() with your surface triangle data.
 *   3. Inspect the result with NGW_GetNumPoints() / NGW_GetNumTets().
 *   4. Copy the mesh data using NGW_GetPoints() / NGW_GetTets().
 *   5. Release the result with NGW_FreeResult().
 *   6. Call NGW_Exit() when done.
 */

#ifndef NETGEN_WRAPPER_H
#define NETGEN_WRAPPER_H

#ifdef _WIN32
  #ifdef NGWRAPPER_EXPORTS
    #define NGWRAPPER_API __declspec(dllexport)
  #else
    #define NGWRAPPER_API __declspec(dllimport)
  #endif
#else
  #define NGWRAPPER_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* -----------------------------------------------------------------------
 * Library lifecycle
 * --------------------------------------------------------------------- */

/** Initialise the netgen meshing kernel. Must be called before any other
 *  NGW_* function. */
NGWRAPPER_API void NGW_Init(void);

/** Shut down the netgen meshing kernel cleanly. */
NGWRAPPER_API void NGW_Exit(void);

/* -----------------------------------------------------------------------
 * Mesh generation
 * --------------------------------------------------------------------- */

/**
 * Generate a tetrahedral mesh from a closed triangulated surface.
 *
 * \param numVertices   Number of vertices in the surface mesh.
 * \param vertices      Flat array of vertex coordinates:
 *                      [x0,y0,z0, x1,y1,z1, …]  (length = numVertices * 3)
 * \param numTriangles  Number of triangles in the surface mesh.
 * \param triangles     Flat array of triangle vertex indices (0-based):
 *                      [i0,j0,k0, i1,j1,k1, …]  (length = numTriangles * 3)
 * \param maxh          Maximum allowed mesh element size (use 1e6 for unconstrained).
 * \param fineness      Mesh density in [0,1] (0 = coarse, 1 = fine).
 * \param grading       Grading factor in [0,1] (0 = uniform, 1 = aggressive).
 *
 * \return Opaque handle to a mesh result object, or NULL on failure.
 *         The caller is responsible for releasing this via NGW_FreeResult().
 */
NGWRAPPER_API void* NGW_GenerateTetrahedralMesh(
    int     numVertices,
    double* vertices,
    int     numTriangles,
    int*    triangles,
    double  maxh,
    double  fineness,
    double  grading);

/* -----------------------------------------------------------------------
 * Result queries  (all take the handle returned by NGW_GenerateTetrahedralMesh)
 * --------------------------------------------------------------------- */

/** Returns the number of mesh vertices in the result, or 0 on NULL handle. */
NGWRAPPER_API int NGW_GetNumPoints(void* result);

/** Returns the number of tetrahedral elements in the result, or 0 on NULL handle. */
NGWRAPPER_API int NGW_GetNumTets(void* result);

/**
 * Copy vertex coordinates into a caller-supplied buffer.
 *
 * \param result      Handle returned by NGW_GenerateTetrahedralMesh().
 * \param outVertices Buffer of length NGW_GetNumPoints(result) * 3.
 *                    Layout: [x0,y0,z0, x1,y1,z1, …]
 */
NGWRAPPER_API void NGW_GetPoints(void* result, double* outVertices);

/**
 * Copy tetrahedral element indices into a caller-supplied buffer.
 *
 * Indices are 0-based and refer to the vertex array returned by NGW_GetPoints().
 *
 * \param result  Handle returned by NGW_GenerateTetrahedralMesh().
 * \param outTets Buffer of length NGW_GetNumTets(result) * 4.
 *                Layout: [a0,b0,c0,d0, a1,b1,c1,d1, …]
 */
NGWRAPPER_API void NGW_GetTets(void* result, int* outTets);

/**
 * Release a mesh result previously returned by NGW_GenerateTetrahedralMesh().
 * Passing NULL is safe and has no effect.
 */
NGWRAPPER_API void NGW_FreeResult(void* result);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // NETGEN_WRAPPER_H

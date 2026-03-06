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
 *   2. Fill an NGW_MeshingParams (start from NGW_DefaultMeshingParams()).
 *   3. (Optional) Build NGW_PointSizeRestriction / NGW_BoxSizeRestriction arrays.
 *   4. Call NGW_GenerateTetrahedralMesh() or NGW_GenerateTetrahedralMeshEx().
 *   5. Inspect the result with NGW_GetNumPoints() / NGW_GetNumTets().
 *   6. Copy the mesh data using NGW_GetPoints() / NGW_GetTets().
 *   7. Release the result with NGW_FreeResult().
 *   8. Call NGW_Exit() when done.
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

/* =========================================================================
 * Parameter and restriction structures
 * ======================================================================= */

/*!
 * All meshing parameters in a single blittable struct.
 *
 * Fields are ordered to avoid implicit padding (all doubles first, then ints)
 * so that the layout matches a C# StructLayout(Sequential) struct without
 * any special Pack attribute.
 */
typedef struct NGW_MeshingParams
{
    /* --- Element size -------------------------------------------------- */
    double maxh;             /*!< Maximum global element size (default 1e6) */
    double minh;             /*!< Minimum global element size (default 0)   */

    /* --- Density / grading --------------------------------------------- */
    double fineness;         /*!< Mesh density [0=coarse … 1=fine] (0.5)   */
    double grading;          /*!< Grading [0=uniform … 1=aggressive] (0.3) */

    /* --- Curvature / edge resolution ----------------------------------- */
    double elementsperedge;  /*!< Elements per geometry edge (2.0)          */
    double elementspercurve; /*!< Elements per curvature radius (2.0)       */

    /* --- Close-edge refinement ----------------------------------------- */
    double closeedgefact;    /*!< Refinement factor at close edges (2.0)    */

    /* --- Minimum edge length enforcement ------------------------------- */
    double minedgelen;       /*!< Minimum edge length when enforced (1e-4)  */

    /* --- Integer flags (grouped to avoid padding) ---------------------- */
    int    closeedgeenable;   /*!< Enable close-edge refinement (0)         */
    int    minedgelenenable;  /*!< Enforce minimum edge length (0)          */
    int    optsteps_2d;       /*!< 2-D optimisation steps (3)               */
    int    optsteps_3d;       /*!< 3-D optimisation steps (3)               */
    int    optsurfmeshenable; /*!< Enable surface mesh optimisation (1)     */
    int    optvolmeshenable;  /*!< Enable volume mesh optimisation (1)      */
} NGW_MeshingParams;


/*!
 * Restrict the maximum mesh element size at a single point.
 *
 * Netgen will use size \c h or smaller for all elements near (x, y, z).
 */
typedef struct NGW_PointSizeRestriction
{
    double x;  /*!< X coordinate of the restriction point */
    double y;  /*!< Y coordinate of the restriction point */
    double z;  /*!< Z coordinate of the restriction point */
    double h;  /*!< Maximum element size at this point    */
} NGW_PointSizeRestriction;


/*!
 * Restrict the maximum mesh element size inside an axis-aligned box.
 *
 * All elements whose vertices fall inside the box [pmin, pmax] will
 * have size \c h or smaller.
 */
typedef struct NGW_BoxSizeRestriction
{
    double xmin; /*!< Box minimum X */
    double ymin; /*!< Box minimum Y */
    double zmin; /*!< Box minimum Z */
    double xmax; /*!< Box maximum X */
    double ymax; /*!< Box maximum Y */
    double zmax; /*!< Box maximum Z */
    double h;    /*!< Maximum element size inside the box */
} NGW_BoxSizeRestriction;


/* =========================================================================
 * Library lifecycle
 * ======================================================================= */

/** Initialise the netgen meshing kernel. Must be called before any other
 *  NGW_* function. */
NGWRAPPER_API void NGW_Init(void);

/** Shut down the netgen meshing kernel cleanly. */
NGWRAPPER_API void NGW_Exit(void);

/* =========================================================================
 * Parameter helpers
 * ======================================================================= */

/**
 * Fill \p mp with the default netgen meshing parameters.
 *
 * Always call this before customising individual fields so that any fields
 * added in future versions are properly initialised.
 */
NGWRAPPER_API void NGW_DefaultMeshingParams(NGW_MeshingParams* mp);

/* =========================================================================
 * Mesh generation
 * ======================================================================= */

/**
 * Generate a tetrahedral mesh from a closed triangulated surface.
 *
 * \param numVertices   Number of vertices in the surface mesh.
 * \param vertices      Flat array [x0,y0,z0, …]  (length = numVertices * 3)
 * \param numTriangles  Number of triangles.
 * \param triangles     Flat 0-based index array [i0,j0,k0, …]
 *                      (length = numTriangles * 3)
 * \param mp            Pointer to meshing parameters (may not be NULL).
 *
 * \return Opaque handle to a mesh result, or NULL on failure.
 *         Release with NGW_FreeResult().
 */
NGWRAPPER_API void* NGW_GenerateTetrahedralMesh(
    int                      numVertices,
    double*                  vertices,
    int                      numTriangles,
    int*                     triangles,
    const NGW_MeshingParams* mp);

/**
 * Generate a tetrahedral mesh with additional local size restrictions.
 *
 * Same as NGW_GenerateTetrahedralMesh() but also applies point- and
 * box-based mesh-size restrictions before meshing.
 *
 * \param numVertices         Number of surface vertices.
 * \param vertices            Flat [x,y,z, …] array.
 * \param numTriangles        Number of triangles.
 * \param triangles           Flat 0-based index array.
 * \param mp                  Meshing parameters (may not be NULL).
 * \param numPointRestrictions Number of point-based restrictions (0 = none).
 * \param pointRestrictions   Array of NGW_PointSizeRestriction, or NULL.
 * \param numBoxRestrictions  Number of box-based restrictions (0 = none).
 * \param boxRestrictions     Array of NGW_BoxSizeRestriction, or NULL.
 *
 * \return Opaque handle to a mesh result, or NULL on failure.
 */
NGWRAPPER_API void* NGW_GenerateTetrahedralMeshEx(
    int                            numVertices,
    double*                        vertices,
    int                            numTriangles,
    int*                           triangles,
    const NGW_MeshingParams*       mp,
    int                            numPointRestrictions,
    const NGW_PointSizeRestriction* pointRestrictions,
    int                            numBoxRestrictions,
    const NGW_BoxSizeRestriction*  boxRestrictions);

/* =========================================================================
 * Result queries  (handle returned by NGW_Generate*)
 * ======================================================================= */

/** Number of mesh vertices in the result (0 on NULL handle). */
NGWRAPPER_API int NGW_GetNumPoints(void* result);

/** Number of tetrahedral elements in the result (0 on NULL handle). */
NGWRAPPER_API int NGW_GetNumTets(void* result);

/**
 * Copy vertex coordinates into a caller-supplied buffer.
 * Buffer length must be NGW_GetNumPoints(result) * 3.
 * Layout: [x0,y0,z0, x1,y1,z1, …]
 */
NGWRAPPER_API void NGW_GetPoints(void* result, double* outVertices);

/**
 * Copy tetrahedral element indices (0-based) into a caller-supplied buffer.
 * Buffer length must be NGW_GetNumTets(result) * 4.
 * Layout: [a0,b0,c0,d0, a1,b1,c1,d1, …]
 */
NGWRAPPER_API void NGW_GetTets(void* result, int* outTets);

/**
 * Release a mesh result. Passing NULL is safe and has no effect.
 */
NGWRAPPER_API void NGW_FreeResult(void* result);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // NETGEN_WRAPPER_H

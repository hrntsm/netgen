/*!
 * \file netgen_wrapper.cpp
 * \brief Implementation of the flat C wrapper for netgen's STL-based
 *        tetrahedral mesh generation.
 */

#include "netgen_wrapper.h"

#include <cmath>
#include <cstring>
#include <vector>

// Include netgen's C API inside its own namespace to avoid symbol clashes.
namespace nglib {
#include <nglib.h>
}

using namespace nglib;

// ---------------------------------------------------------------------------
// Global state
// ---------------------------------------------------------------------------

static NGW_ProgressCallback s_progressCallback = nullptr;

#ifdef _WIN32
  static __declspec(thread) int s_lastError = 0;
#else
  static __thread int s_lastError = 0;
#endif

// ---------------------------------------------------------------------------
// Internal result type
// ---------------------------------------------------------------------------

struct NgMeshResult
{
    std::vector<double> vertices;    ///< Flat [x,y,z, …] array, 0-based
    std::vector<int>    tets;        ///< Flat element node indices, 0-based
    int                 nodesPerElement; ///< 4 (TET4) or 10 (TET10)

    NgMeshResult() : nodesPerElement(4) {}
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// Compute the un-normalised cross product of (p2-p1) × (p3-p1).
static void computeNormal(const double* p1, const double* p2, const double* p3,
                           double* nv)
{
    double ax = p2[0] - p1[0], ay = p2[1] - p1[1], az = p2[2] - p1[2];
    double bx = p3[0] - p1[0], by = p3[1] - p1[1], bz = p3[2] - p1[2];
    nv[0] = ay * bz - az * by;
    nv[1] = az * bx - ax * bz;
    nv[2] = ax * by - ay * bx;
}

/// Transfer fields from NGW_MeshingParams into an Ng_Meshing_Parameters.
static void applyParams(const NGW_MeshingParams* src, Ng_Meshing_Parameters& dst)
{
    dst.maxh               = src->maxh;
    dst.minh               = src->minh;
    dst.fineness           = src->fineness;
    dst.grading            = src->grading;
    dst.elementsperedge    = src->elementsperedge;
    dst.elementspercurve   = src->elementspercurve;
    dst.closeedgeenable    = src->closeedgeenable;
    dst.closeedgefact      = src->closeedgefact;
    dst.minedgelenenable   = src->minedgelenenable;
    dst.minedgelen         = src->minedgelen;
    dst.optsteps_2d        = src->optsteps_2d;
    dst.optsteps_3d        = src->optsteps_3d;
    dst.second_order       = 0; // handled separately via Ng_Generate_SecondOrder
    dst.quad_dominated     = 0;
    dst.meshsize_filename  = nullptr;
    dst.uselocalh          = 1;
    dst.optsurfmeshenable  = src->optsurfmeshenable;
    dst.optvolmeshenable   = src->optvolmeshenable;
    dst.invert_tets        = 0;
    dst.invert_trigs       = 0;
    dst.check_overlap              = 1;
    dst.check_overlapping_boundary = 1;
}

/// Fire progress callback if one is registered.
static void fireProgress(const char* stage, int percent)
{
    if (s_progressCallback)
        s_progressCallback(stage, percent);
}

// ---------------------------------------------------------------------------
// Core mesh-generation logic (shared by both public entry points)
// ---------------------------------------------------------------------------

static void* generateMeshImpl(
    int     numVertices,
    double* vertices,
    int     numTriangles,
    int*    triangles,
    const NGW_MeshingParams*        mp,
    int                             numPointRestrictions,
    const NGW_PointSizeRestriction* pointRestrictions,
    int                             numBoxRestrictions,
    const NGW_BoxSizeRestriction*   boxRestrictions)
{
    s_lastError = NGW_OK;

    if (numVertices <= 0 || numTriangles <= 0
        || vertices == nullptr || triangles == nullptr || mp == nullptr)
    {
        s_lastError = NGW_ERROR_INVALID_INPUT;
        return nullptr;
    }

    // ------------------------------------------------------------------
    // 1. Build STL geometry from the caller's triangle soup
    // ------------------------------------------------------------------
    Ng_STL_Geometry* stlGeom = Ng_STL_NewGeometry();
    if (!stlGeom)
        return nullptr;

    for (int i = 0; i < numTriangles; ++i)
    {
        int i0 = triangles[i * 3 + 0];
        int i1 = triangles[i * 3 + 1];
        int i2 = triangles[i * 3 + 2];

        if (i0 < 0 || i0 >= numVertices ||
            i1 < 0 || i1 >= numVertices ||
            i2 < 0 || i2 >= numVertices)
            continue;

        double p1[3] = { vertices[i0*3], vertices[i0*3+1], vertices[i0*3+2] };
        double p2[3] = { vertices[i1*3], vertices[i1*3+1], vertices[i1*3+2] };
        double p3[3] = { vertices[i2*3], vertices[i2*3+1], vertices[i2*3+2] };

        double nv[3];
        computeNormal(p1, p2, p3, nv);
        Ng_STL_AddTriangle(stlGeom, p1, p2, p3, nv);
    }

    Ng_Result res = Ng_STL_InitSTLGeometry(stlGeom);
    if (res != NG_OK)
    {
        s_lastError = NGW_ERROR_STL_INIT_FAILED;
        return nullptr;
    }

    // ------------------------------------------------------------------
    // 2. Set up meshing parameters
    // ------------------------------------------------------------------
    Ng_Meshing_Parameters ngmp;
    applyParams(mp, ngmp);

    // ------------------------------------------------------------------
    // 3. Create mesh and apply optional local size restrictions
    // ------------------------------------------------------------------
    Ng_Mesh* mesh = Ng_NewMesh();
    if (!mesh)
        return nullptr;

    // Global size restriction derived from maxh (belt-and-suspenders)
    if (mp->maxh < 1e5)
        Ng_RestrictMeshSizeGlobal(mesh, mp->maxh);

    // Point-based restrictions
    if (numPointRestrictions > 0 && pointRestrictions != nullptr)
    {
        for (int i = 0; i < numPointRestrictions; ++i)
        {
            double p[3] = {
                pointRestrictions[i].x,
                pointRestrictions[i].y,
                pointRestrictions[i].z
            };
            Ng_RestrictMeshSizePoint(mesh, p, pointRestrictions[i].h);
        }
    }

    // Box-based restrictions
    if (numBoxRestrictions > 0 && boxRestrictions != nullptr)
    {
        for (int i = 0; i < numBoxRestrictions; ++i)
        {
            double pmin[3] = {
                boxRestrictions[i].xmin,
                boxRestrictions[i].ymin,
                boxRestrictions[i].zmin
            };
            double pmax[3] = {
                boxRestrictions[i].xmax,
                boxRestrictions[i].ymax,
                boxRestrictions[i].zmax
            };
            Ng_RestrictMeshSizeBox(mesh, pmin, pmax, boxRestrictions[i].h);
        }
    }

    // ------------------------------------------------------------------
    // 4. Run meshing pipeline
    // ------------------------------------------------------------------
    fireProgress("edges", 10);
    res = Ng_STL_MakeEdges(stlGeom, mesh, &ngmp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        s_lastError = NGW_ERROR_EDGE_GENERATION_FAILED;
        return nullptr;
    }

    fireProgress("surface", 35);
    res = Ng_STL_GenerateSurfaceMesh(stlGeom, mesh, &ngmp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        s_lastError = NGW_ERROR_SURFACE_MESH_FAILED;
        return nullptr;
    }

    fireProgress("volume", 60);
    res = Ng_GenerateVolumeMesh(mesh, &ngmp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        s_lastError = NGW_ERROR_VOLUME_MESH_FAILED;
        return nullptr;
    }

    // ------------------------------------------------------------------
    // 4a. Optional: second-order elements (TET10)
    // ------------------------------------------------------------------
    if (mp->second_order)
    {
        fireProgress("second_order", 75);
        Ng_Generate_SecondOrder(mesh);
    }

    // ------------------------------------------------------------------
    // 4b. Optional: uniform refinement
    // ------------------------------------------------------------------
    if (mp->uniform_ref_steps > 0)
    {
        fireProgress("refinement", 80);
        for (int r = 0; r < mp->uniform_ref_steps; ++r)
            Ng_STL_Uniform_Refinement(stlGeom, mesh);
    }

    fireProgress("extract", 90);

    // ------------------------------------------------------------------
    // 5. Extract result (convert from 1-based netgen indices to 0-based)
    // ------------------------------------------------------------------
    NgMeshResult* result = new NgMeshResult();
    result->nodesPerElement = mp->second_order ? 10 : 4;

    int np = Ng_GetNP(mesh);
    result->vertices.resize(static_cast<size_t>(np) * 3);
    for (int i = 1; i <= np; ++i)
    {
        double x[3];
        Ng_GetPoint(mesh, i, x);
        result->vertices[(i - 1) * 3 + 0] = x[0];
        result->vertices[(i - 1) * 3 + 1] = x[1];
        result->vertices[(i - 1) * 3 + 2] = x[2];
    }

    int ne = Ng_GetNE(mesh);
    int npe = result->nodesPerElement;
    result->tets.resize(static_cast<size_t>(ne) * npe);
    for (int i = 1; i <= ne; ++i)
    {
        int pi[10] = {};
        Ng_GetVolumeElement(mesh, i, pi);
        for (int k = 0; k < npe; ++k)
            result->tets[(i - 1) * npe + k] = pi[k] - 1;
    }

    Ng_DeleteMesh(mesh);
    fireProgress("done", 100);
    return static_cast<void*>(result);
}

// ---------------------------------------------------------------------------
// Public C API implementation
// ---------------------------------------------------------------------------

extern "C" {

NGWRAPPER_API void NGW_SetProgressCallback(NGW_ProgressCallback callback)
{
    s_progressCallback = callback;
}

NGWRAPPER_API void NGW_ClearProgressCallback(void)
{
    s_progressCallback = nullptr;
}

NGWRAPPER_API int NGW_GetLastError(void)
{
    return s_lastError;
}

NGWRAPPER_API void NGW_Init(void)
{
    Ng_Init();
}

NGWRAPPER_API void NGW_Exit(void)
{
    Ng_Exit();
}

NGWRAPPER_API void NGW_DefaultMeshingParams(NGW_MeshingParams* mp)
{
    if (!mp) return;
    mp->maxh             = 1e6;
    mp->minh             = 0.0;
    mp->fineness         = 0.5;
    mp->grading          = 0.3;
    mp->elementsperedge  = 2.0;
    mp->elementspercurve = 2.0;
    mp->closeedgefact    = 2.0;
    mp->minedgelen       = 1e-4;
    mp->closeedgeenable   = 0;
    mp->minedgelenenable  = 0;
    mp->optsteps_2d       = 3;
    mp->optsteps_3d       = 3;
    mp->optsurfmeshenable = 1;
    mp->optvolmeshenable  = 1;
    mp->second_order      = 0;
    mp->uniform_ref_steps = 0;
}

NGWRAPPER_API void* NGW_GenerateTetrahedralMesh(
    int                      numVertices,
    double*                  vertices,
    int                      numTriangles,
    int*                     triangles,
    const NGW_MeshingParams* mp)
{
    return generateMeshImpl(numVertices, vertices, numTriangles, triangles,
                            mp, 0, nullptr, 0, nullptr);
}

NGWRAPPER_API void* NGW_GenerateTetrahedralMeshEx(
    int                            numVertices,
    double*                        vertices,
    int                            numTriangles,
    int*                           triangles,
    const NGW_MeshingParams*       mp,
    int                            numPointRestrictions,
    const NGW_PointSizeRestriction* pointRestrictions,
    int                            numBoxRestrictions,
    const NGW_BoxSizeRestriction*  boxRestrictions)
{
    return generateMeshImpl(numVertices, vertices, numTriangles, triangles,
                            mp,
                            numPointRestrictions, pointRestrictions,
                            numBoxRestrictions,   boxRestrictions);
}

NGWRAPPER_API int NGW_GetNumPoints(void* result)
{
    if (!result) return 0;
    return static_cast<int>(
        reinterpret_cast<NgMeshResult*>(result)->vertices.size() / 3);
}

NGWRAPPER_API int NGW_GetNumTets(void* result)
{
    if (!result) return 0;
    const NgMeshResult* r = reinterpret_cast<NgMeshResult*>(result);
    int npe = r->nodesPerElement > 0 ? r->nodesPerElement : 4;
    return static_cast<int>(r->tets.size() / npe);
}

NGWRAPPER_API int NGW_GetNodesPerElement(void* result)
{
    if (!result) return 0;
    return reinterpret_cast<NgMeshResult*>(result)->nodesPerElement;
}

NGWRAPPER_API void NGW_GetPoints(void* result, double* outVertices)
{
    if (!result || !outVertices) return;
    const auto& v = reinterpret_cast<NgMeshResult*>(result)->vertices;
    std::memcpy(outVertices, v.data(), v.size() * sizeof(double));
}

NGWRAPPER_API void NGW_GetTets(void* result, int* outTets)
{
    if (!result || !outTets) return;
    const auto& t = reinterpret_cast<NgMeshResult*>(result)->tets;
    std::memcpy(outTets, t.data(), t.size() * sizeof(int));
}

NGWRAPPER_API void NGW_FreeResult(void* result)
{
    delete reinterpret_cast<NgMeshResult*>(result);
}

} // extern "C"

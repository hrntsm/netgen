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
// Internal result type
// ---------------------------------------------------------------------------

struct NgMeshResult
{
    std::vector<double> vertices; ///< Flat [x,y,z, …] array, 0-based
    std::vector<int>    tets;     ///< Flat [a,b,c,d, …] array, 0-based
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
    dst.second_order       = 0;
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
    if (numVertices <= 0 || numTriangles <= 0
        || vertices == nullptr || triangles == nullptr || mp == nullptr)
        return nullptr;

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
        return nullptr;

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
    res = Ng_STL_MakeEdges(stlGeom, mesh, &ngmp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        return nullptr;
    }

    res = Ng_STL_GenerateSurfaceMesh(stlGeom, mesh, &ngmp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        return nullptr;
    }

    res = Ng_GenerateVolumeMesh(mesh, &ngmp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        return nullptr;
    }

    // ------------------------------------------------------------------
    // 5. Extract result (convert from 1-based netgen indices to 0-based)
    // ------------------------------------------------------------------
    NgMeshResult* result = new NgMeshResult();

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
    result->tets.resize(static_cast<size_t>(ne) * 4);
    for (int i = 1; i <= ne; ++i)
    {
        int pi[10] = {};
        Ng_GetVolumeElement(mesh, i, pi);
        result->tets[(i - 1) * 4 + 0] = pi[0] - 1;
        result->tets[(i - 1) * 4 + 1] = pi[1] - 1;
        result->tets[(i - 1) * 4 + 2] = pi[2] - 1;
        result->tets[(i - 1) * 4 + 3] = pi[3] - 1;
    }

    Ng_DeleteMesh(mesh);
    return static_cast<void*>(result);
}

// ---------------------------------------------------------------------------
// Public C API implementation
// ---------------------------------------------------------------------------

extern "C" {

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
    return static_cast<int>(
        reinterpret_cast<NgMeshResult*>(result)->tets.size() / 4);
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

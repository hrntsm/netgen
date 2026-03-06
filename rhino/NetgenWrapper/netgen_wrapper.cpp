/*!
 * \file netgen_wrapper.cpp
 * \brief Implementation of the flat C wrapper for netgen's STL-based
 *        tetrahedral mesh generation.
 *
 * Strategy
 * --------
 * 1. Accept the caller's triangulated surface (vertices + triangle indices).
 * 2. Build an Ng_STL_Geometry by adding each triangle.
 * 3. Run the standard netgen STL pipeline:
 *      Ng_STL_InitSTLGeometry → Ng_STL_MakeEdges →
 *      Ng_STL_GenerateSurfaceMesh → Ng_GenerateVolumeMesh
 * 4. Extract all points and tet elements into a heap-allocated result
 *    struct that the caller can query and then free.
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

NGWRAPPER_API void* NGW_GenerateTetrahedralMesh(
    int     numVertices,
    double* vertices,
    int     numTriangles,
    int*    triangles,
    double  maxh,
    double  fineness,
    double  grading)
{
    if (numVertices <= 0 || numTriangles <= 0
        || vertices == nullptr || triangles == nullptr)
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

        // Guard against out-of-range indices
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

    // ------------------------------------------------------------------
    // 2. Initialise STL geometry
    // ------------------------------------------------------------------
    Ng_Result res = Ng_STL_InitSTLGeometry(stlGeom);
    if (res != NG_OK)
        return nullptr;

    // ------------------------------------------------------------------
    // 3. Set up meshing parameters
    // ------------------------------------------------------------------
    Ng_Meshing_Parameters mp;
    mp.uselocalh          = 1;
    mp.maxh               = maxh;
    mp.minh               = 0.0;
    mp.fineness           = fineness;
    mp.grading            = grading;
    mp.elementsperedge    = 2.0;
    mp.elementspercurve   = 2.0;
    mp.closeedgeenable    = 0;
    mp.closeedgefact      = 2.0;
    mp.minedgelenenable   = 0;
    mp.minedgelen         = 1e-4;
    mp.second_order       = 0;
    mp.quad_dominated     = 0;
    mp.meshsize_filename  = nullptr;
    mp.optsurfmeshenable  = 1;
    mp.optvolmeshenable   = 1;
    mp.optsteps_2d        = 3;
    mp.optsteps_3d        = 3;
    mp.invert_tets        = 0;
    mp.invert_trigs       = 0;
    mp.check_overlap              = 1;
    mp.check_overlapping_boundary = 1;

    // ------------------------------------------------------------------
    // 4. Run meshing pipeline
    // ------------------------------------------------------------------
    Ng_Mesh* mesh = Ng_NewMesh();
    if (!mesh)
        return nullptr;

    res = Ng_STL_MakeEdges(stlGeom, mesh, &mp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        return nullptr;
    }

    res = Ng_STL_GenerateSurfaceMesh(stlGeom, mesh, &mp);
    if (res != NG_OK)
    {
        Ng_DeleteMesh(mesh);
        return nullptr;
    }

    res = Ng_GenerateVolumeMesh(mesh, &mp);
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

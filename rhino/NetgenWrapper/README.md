# netgen_wrapper – C shared library for P/Invoke

A thin C wrapper around [netgen](https://ngsolve.org)'s STL-based tetrahedral
mesh generation pipeline.  Its sole purpose is to expose a **flat C API** that
can be called from any language with P/Invoke or FFI support (C#, Python ctypes,
Rust, …) without requiring callers to deal with C++ ABI details.

## API overview

```c
// Initialise / shut down
void NGW_Init(void);
void NGW_Exit(void);

// Generate tets from a closed triangulated surface
//   vertices  – flat [x,y,z, …]  length numVertices*3
//   triangles – flat [i,j,k, …]  length numTriangles*3  (0-based)
//   maxh      – max element size (1e6 = unconstrained)
//   fineness  – [0,1]  0=coarse, 1=fine
//   grading   – [0,1]  0=uniform, 1=aggressive
// Returns an opaque handle or NULL on failure.
void* NGW_GenerateTetrahedralMesh(
    int numVertices, double* vertices,
    int numTriangles, int* triangles,
    double maxh, double fineness, double grading);

// Query result
int  NGW_GetNumPoints(void* result);
int  NGW_GetNumTets(void* result);
void NGW_GetPoints(void* result, double* outVertices);  // length = GetNumPoints*3
void NGW_GetTets  (void* result, int*    outTets);      // length = GetNumTets*4  (0-based)

// Release result
void NGW_FreeResult(void* result);
```

## Build

```bash
# From the repository root – build netgen first
cmake -S . -B build -DCMAKE_INSTALL_PREFIX=/opt/netgen
cmake --build build --target nglib -j$(nproc)
cmake --install build

# Then build the wrapper
cmake -S rhino/NetgenWrapper -B rhino/NetgenWrapper/build \
      -DCMAKE_PREFIX_PATH=/opt/netgen
cmake --build rhino/NetgenWrapper/build -j$(nproc)
```

Or supply explicit paths when not using the installed CMake config:

```bash
cmake -S rhino/NetgenWrapper -B rhino/NetgenWrapper/build \
      -DNGLIB_INCLUDE_DIR=/path/to/netgen/nglib \
      -DNGLIB_LIBRARY=/path/to/build/nglib/libnglib.so
cmake --build rhino/NetgenWrapper/build -j$(nproc)
```

The output is `libnetgen_wrapper.so` (Linux), `libnetgen_wrapper.dylib` (macOS),
or `netgen_wrapper.dll` (Windows).

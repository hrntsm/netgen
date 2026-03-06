# RhinoNetgenBridge – C# P/Invoke library

A .NET class library that wraps the `netgen_wrapper` native shared library,
letting you generate **tetrahedral meshes** from Rhino `Brep` objects with a
handful of lines of C#.

## Prerequisites

| Component | Purpose |
|-----------|---------|
| [netgen](https://ngsolve.org) (built as a shared library) | Meshing kernel |
| `netgen_wrapper` shared library (see `../NetgenWrapper/`) | Flat C API |
| Rhino 7 / 8 (or the `RhinoCommon` NuGet package) | Geometry types |
| .NET SDK ≥ 5 (or .NET Framework 4.8 for Windows) | Build toolchain |

The native library `netgen_wrapper` (`.dll` / `.so` / `.dylib`) must be on
the DLL search path at runtime:

- **Windows** – place `netgen_wrapper.dll` next to your `.exe` / plugin `.rhp`.
- **Linux/macOS** – place `libnetgen_wrapper.so/.dylib` in `LD_LIBRARY_PATH`.

## Quick start

```csharp
using RhinoNetgenBridge;
using Rhino.Geometry;

// 1. Initialise (once per application)
NetgenMesher.Initialize();

// 2. Obtain a closed Brep from somewhere
Brep brep = …;

// 3. Generate
var mp = MeshingParameters.Fine();   // or .Coarse() / .Medium() / .VeryFine()
TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp);

if (tet == null)
{
    RhinoApp.WriteLine("Meshing failed.");
    return;
}

RhinoApp.WriteLine($"Vertices : {tet.VertexCount}");
RhinoApp.WriteLine($"Tets     : {tet.TetCount}");

// 4. (Optional) display the surface of the tet mesh in Rhino
Mesh surface = tet.ToRhinoSurfaceMesh();
doc.Objects.AddMesh(surface);
doc.Views.Redraw();

// 5. Shutdown (on application exit)
NetgenMesher.Shutdown();
```

### Working with the raw data

```csharp
// Flat vertex array  [x0,y0,z0, x1,y1,z1, …]
double[] verts = tet.Vertices;

// Flat tet index array  [a,b,c,d, …]  (0-based)
int[] tets = tet.Tetrahedra;

// Individual accessors
Point3d pt = tet.GetVertex(42);
var (a, b, c, d) = tet.GetTetrahedron(0);
```

### Meshing directly from a Mesh

If you already have a closed triangulated `Mesh` (e.g. from STL import):

```csharp
TetrahedralMesh tet = NetgenMesher.GenerateFromMesh(closedMesh, mp);
```

## Build

```bash
cd rhino/RhinoNetgenBridge
dotnet build -c Release
```

## Project structure

```
RhinoNetgenBridge/
├── NetgenNative.cs       P/Invoke declarations (internal)
├── NetgenMesher.cs       Public API
├── TetrahedralMesh.cs    Result type
├── MeshingParameters.cs  Parameters
└── RhinoNetgenBridge.csproj
```

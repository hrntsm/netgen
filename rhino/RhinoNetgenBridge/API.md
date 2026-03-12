# RhinoNetgenBridge C# API

`RhinoNetgenBridge` を配布済み DLL と関連ライブラリから使うための最小ガイドです。  
Rhino / C# 側から四面体メッシュ生成を呼び出すケースを想定しています。

## 1. 必要ファイル

`RhinoNetgenBridge.dll` と同じフォルダに、少なくとも次を配置してください。

- `RhinoNetgenBridge.dll`
- `RhinoNetgenBridge.deps.json`
- `libnetgen_wrapper.dylib`
- `libnglib.dylib`
- `libngcore.dylib`

補足:

- 配布 DLL は `.NET 8` を前提にしています。
- `RhinoCommon` は配布物に含めません。Rhino 上で使う場合は Rhino 側、通常の C# プロジェクトで使う場合は参照側で解決してください。

## 2. 使い始めの流れ

基本フローは次のとおりです。

1. `NetgenMesher.Initialize()` を一度だけ呼ぶ
2. `Brep` または `Mesh` を渡して四面体メッシュを生成する
3. `TetrahedralMesh` から頂点・要素・品質情報を取得する
4. 必要なら `MeshExporter` で外部形式へ保存する
5. 終了時に `NetgenMesher.Shutdown()` を呼ぶ

最小サンプル:

```csharp
using Rhino.Geometry;
using RhinoNetgenBridge;

NetgenMesher.Initialize();

var mp = MeshingParameters.Fine();
mp.SecondOrder = false;
mp.UniformRefinementSteps = 1;

TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp);

RhinoApp.WriteLine($"Vertices: {tet.VertexCount}");
RhinoApp.WriteLine($"Tets: {tet.TetCount}");

Mesh surface = tet.ToRhinoSurfaceMesh();

NetgenMesher.Shutdown();
```

## 3. 主な API

### NetgenMesher

メッシュ生成の入口です。

```csharp
public static void Initialize()
public static void Shutdown()

public static TetrahedralMesh GenerateFromBrep(
    Brep brep,
    MeshingParameters? meshingParams = null,
    Rhino.Geometry.MeshingParameters? rhinoMeshParams = null,
    IReadOnlyList<PointSizeRestriction>? pointRestrictions = null,
    IReadOnlyList<BoxSizeRestriction>? boxRestrictions = null,
    Action<string, int>? onProgress = null)

public static TetrahedralMesh GenerateFromBreps(
    IReadOnlyList<Brep> breps,
    MeshingParameters? meshingParams = null,
    Rhino.Geometry.MeshingParameters? rhinoMeshParams = null,
    IReadOnlyList<PointSizeRestriction>? pointRestrictions = null,
    IReadOnlyList<BoxSizeRestriction>? boxRestrictions = null,
    Action<string, int>? onProgress = null)

public static TetrahedralMesh GenerateFromMesh(
    Mesh surfaceMesh,
    MeshingParameters? meshingParams = null,
    IReadOnlyList<PointSizeRestriction>? pointRestrictions = null,
    IReadOnlyList<BoxSizeRestriction>? boxRestrictions = null,
    Action<string, int>? onProgress = null)
```

使い分け:

- `GenerateFromBrep`: 単一の閉じた Brep から生成
- `GenerateFromBreps`: 複数 Brep をまとめて 1 つの体積として生成
- `GenerateFromMesh`: すでに三角形化済みの閉曲面メッシュから生成

`onProgress` には `"edges"`, `"surface"`, `"volume"`, `"done"` などの段階名と進捗率が入ります。

### MeshingParameters

Netgen 側のパラメータです。まずはプリセットから始めるのが簡単です。

```csharp
MeshingParameters.Coarse()
MeshingParameters.Medium()
MeshingParameters.Fine()
MeshingParameters.VeryFine()
```

よく使うプロパティ:

- `MaxElementSize`, `MinElementSize`
- `Fineness`, `Grading`
- `ElementsPerEdge`, `ElementsPerCurve`
- `EnableSurfaceOptimization`, `EnableVolumeOptimization`
- `OptimizationSteps2D`, `OptimizationSteps3D`
- `SecondOrder`
- `UniformRefinementSteps`
- `LaplacianSmoothingIterations`, `LaplacianSmoothingFactor`

注意:

- `SecondOrder = true` だと TET10 になります
- `CreateSmoothed` は TET4 のみ対応です

### PointSizeRestriction / BoxSizeRestriction

局所的に細かいメッシュを要求したいときに使います。

```csharp
new PointSizeRestriction(new Point3d(10, 0, 5), 0.5)
new PointSizeRestriction(10, 0, 5, 0.5)

new BoxSizeRestriction(new BoundingBox(min, max), 1.0)
new BoxSizeRestriction(minPoint, maxPoint, 1.0)
```

例:

```csharp
var pointRestrictions = new[]
{
    new PointSizeRestriction(new Point3d(0, 0, 0), 0.25),
};

var boxRestrictions = new[]
{
    new BoxSizeRestriction(
        new Point3d(-10, -10, -10),
        new Point3d(10, 10, 10),
        0.5),
};
```

### TetrahedralMesh

生成結果です。

主要プロパティ:

- `Vertices`: `double[]` のフラット配列 `[x0,y0,z0, x1,y1,z1, ...]`
- `Tetrahedra`: `int[]` のフラット配列
- `NodesPerElement`: `4` または `10`
- `VertexCount`
- `TetCount`

主要メソッド:

```csharp
Point3d GetVertex(int index)
(int A, int B, int C, int D) GetTetrahedron(int index)
int[] GetTetrahedronAllNodes(int index)

TetQuality ComputeElementQuality(int index)
TetQuality[] ComputeAllElementQualities()
MeshQualityStatistics ComputeQualityStatistics()
MeshQualityStatistics ComputeQualityStatistics(out TetQuality[] perElementQualities)

TetrahedralMesh CreateSmoothed(int iterations, double factor = 0.5)
Mesh ToRhinoSurfaceMesh()
```

### MeshValidator

入力メッシュが Netgen に渡せる状態かを事前検査します。

```csharp
MeshValidationResult result = MeshValidator.Validate(surfaceMesh);
if (!result.IsValid)
{
    foreach (var issue in result.Issues)
        RhinoApp.WriteLine(issue.ToString());
}
```

検出できる代表的な問題:

- `NakedEdge`
- `NonManifoldEdge`
- `DegenerateFace`
- `UnusedVertex`

### MeshExporter

四面体メッシュを外部フォーマットへ保存します。

```csharp
MeshExporter.WriteAbaqus(tet, "model.inp");
MeshExporter.WriteVtk(tet, "model.vtu");
MeshExporter.WriteGmsh(tet, "model.msh");
MeshExporter.WriteNastran(tet, "model.bdf");
```

対応形式:

- Abaqus / CalculiX: `.inp`
- VTK Unstructured Grid: `.vtu`
- Gmsh v2: `.msh`
- NASTRAN: `.bdf`

### NetgenMeshingException / NetgenErrorCode

メッシュ生成に失敗すると `NetgenMeshingException` が投げられます。

```csharp
try
{
    var tet = NetgenMesher.GenerateFromBrep(brep, mp);
}
catch (NetgenMeshingException ex)
{
    RhinoApp.WriteLine($"{ex.ErrorCode}: {ex.Message}");
}
```

代表的な `NetgenErrorCode`:

- `InvalidInput`
- `StlInitFailed`
- `EdgeGenerationFailed`
- `SurfaceMeshFailed`
- `VolumeMeshFailed`

## 4. 典型パターン

### Brep から生成

```csharp
var mp = MeshingParameters.Medium();
var tet = NetgenMesher.GenerateFromBrep(brep, mp);
```

### 複数 Brep をまとめて生成

```csharp
var breps = new List<Brep> { brepA, brepB, brepC };
var tet = NetgenMesher.GenerateFromBreps(breps, MeshingParameters.Fine());
```

### 進捗付きで生成

```csharp
var tet = NetgenMesher.GenerateFromMesh(
    surfaceMesh,
    MeshingParameters.Fine(),
    onProgress: (stage, percent) =>
    {
        RhinoApp.WriteLine($"[{percent}%] {stage}");
    });
```

### 品質評価

```csharp
MeshQualityStatistics stats = tet.ComputeQualityStatistics(out TetQuality[] perElement);
RhinoApp.WriteLine(stats.ToString());
```

### 表面だけ Rhino Mesh として取り出す

```csharp
Mesh surface = tet.ToRhinoSurfaceMesh();
doc.Objects.AddMesh(surface);
```

## 5. 注意点

- `Initialize()` 前に生成 API を呼ぶと `InvalidOperationException` になります
- netgen 本体はスレッドセーフではないため、並列に複数メッシュ生成しないでください
- 入力 `Mesh` は閉じた 2-manifold の三角形メッシュが前提です
- `GenerateFromBreps` は複数 Brep をそのまま体積結合するわけではありません
  事前に `BooleanUnion` などで watertight にしておくほうが安定します
- TET10 (`SecondOrder = true`) では `CreateSmoothed()` は使えません

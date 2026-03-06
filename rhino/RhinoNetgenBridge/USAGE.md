# RhinoNetgenBridge 使い方ガイド

netgen メッシュカーネルを使って Rhino の **Brep から四面体メッシュ**を生成する C# ライブラリです。

---

## 目次

1. [セットアップ](#1-セットアップ)
2. [基本的な使い方](#2-基本的な使い方)
3. [メッシュサイズの制御](#3-メッシュサイズの制御)
4. [ローカルサイズ制約](#4-ローカルサイズ制約)
5. [スムージング](#5-スムージング)
6. [結果の取得・利用](#6-結果の取得利用)
7. [パラメータ一覧](#7-パラメータ一覧)

---

## 1. セットアップ

### 必要なもの

| コンポーネント | 用途 |
|---|---|
| netgen (nglib としてビルド) | メッシュ生成エンジン |
| `netgen_wrapper` 共有ライブラリ | C# との P/Invoke ブリッジ |
| Rhino 7 / 8 (または RhinoCommon NuGet) | ジオメトリ型 |
| .NET SDK ≥ 5 / .NET Framework 4.8 | ビルドツールチェーン |

### ライブラリのビルド

```bash
# 1. netgen 本体をビルド
cmake -S . -B build -DCMAKE_INSTALL_PREFIX=/opt/netgen
cmake --build build --target nglib -j$(nproc)
cmake --install build

# 2. C ラッパーをビルド
cmake -S rhino/NetgenWrapper -B rhino/NetgenWrapper/build \
      -DCMAKE_PREFIX_PATH=/opt/netgen
cmake --build rhino/NetgenWrapper/build -j$(nproc)
# → libnetgen_wrapper.so (Linux) / netgen_wrapper.dll (Windows) が生成される

# 3. C# ライブラリをビルド
cd rhino/RhinoNetgenBridge
dotnet build -c Release
```

ネイティブライブラリ (`netgen_wrapper`) は実行時に検索パスに含まれている必要があります。
- **Windows**: 実行ファイルまたは Rhino プラグイン (.rhp) と同じフォルダに `netgen_wrapper.dll` を配置
- **Linux**: `LD_LIBRARY_PATH` に `libnetgen_wrapper.so` のパスを追加

### 初期化・終了処理

```csharp
// アプリ起動時に一度だけ呼ぶ（複数回呼んでも安全）
NetgenMesher.Initialize();

// アプリ終了時に呼ぶ
NetgenMesher.Shutdown();
```

---

## 2. 基本的な使い方

### Brep から四面体メッシュを生成する

```csharp
using RhinoNetgenBridge;
using Rhino.Geometry;

NetgenMesher.Initialize();

// 閉じた Brep を用意する
Brep brep = /* Rhino から取得 */;

// デフォルトパラメータで生成
TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep);

if (tet == null)
{
    RhinoApp.WriteLine("メッシュ生成に失敗しました。Brep が閉じているか確認してください。");
    return;
}

RhinoApp.WriteLine($"頂点数: {tet.VertexCount}");
RhinoApp.WriteLine($"四面体数: {tet.TetCount}");
```

### プリセットを使う

```csharp
// 粗いメッシュ（高速・低要素数）
var tet = NetgenMesher.GenerateFromBrep(brep, MeshingParameters.Coarse());

// 標準メッシュ（バランス型）
var tet = NetgenMesher.GenerateFromBrep(brep, MeshingParameters.Medium());

// 細かいメッシュ（高品質）
var tet = NetgenMesher.GenerateFromBrep(brep, MeshingParameters.Fine());

// 最高精度メッシュ
var tet = NetgenMesher.GenerateFromBrep(brep, MeshingParameters.VeryFine());
```

### Mesh オブジェクトから生成する（STL 等からのインポート時）

```csharp
// 閉じた三角メッシュを直接渡す（quad は自動的に三角化される）
Mesh closedMesh = /* STL 読み込みや Rhino の Mesh オブジェクト */;
TetrahedralMesh tet = NetgenMesher.GenerateFromMesh(closedMesh);
```

---

## 3. メッシュサイズの制御

`MeshingParameters` クラスで要素サイズ・密度・解像度を細かく制御できます。

### グローバルサイズ制限

```csharp
var mp = MeshingParameters.Medium();

// 最大要素サイズを 5.0 に制限（モデル単位と同じ）
mp.MaxElementSize = 5.0;

// 最小要素サイズを指定（小さすぎる要素を防ぐ）
mp.MinElementSize = 0.1;

var tet = NetgenMesher.GenerateFromBrep(brep, mp);
```

### 密度とグレーディング

```csharp
var mp = new MeshingParameters();

// 密度: 0 = 粗い、1 = 細かい
mp.Fineness = 0.7;

// グレーディング: 0 = 均一サイズ、1 = 特徴部で細かく遠くで粗く
// 複雑な形状では 0.3〜0.5 が一般的
mp.Grading = 0.5;
```

### エッジ・曲面の解像度

```csharp
var mp = MeshingParameters.Medium();

// エッジ 1 本あたりの要素数（大きいほどエッジが細かくなる）
mp.ElementsPerEdge = 4.0;

// 曲率半径あたりの要素数（大きいほど曲面が滑らかになる）
mp.ElementsPerCurve = 4.0;
```

### 近接エッジの細分化

隙間の狭い形状で正しくメッシュが切れない場合に有効にします。

```csharp
var mp = MeshingParameters.Fine();

// 近接エッジ検出を有効化
mp.CloseEdgeRefinement = true;

// 近接部の細分化係数（大きいほど細かくなる）
mp.CloseEdgeFactor = 3.0;
```

### 最小エッジ長の強制

```csharp
var mp = MeshingParameters.Fine();

// 短すぎるエッジを作らないよう強制
mp.EnforceMinEdgeLength = true;
mp.MinEdgeLength = 0.01;
```

---

## 4. ローカルサイズ制約

モデル全体を細かくせずに**特定の場所だけ**メッシュを細かくしたい場合に使います。

### 点による制約

```csharp
var mp = MeshingParameters.Medium();
mp.MaxElementSize = 10.0;  // 全体は粗め

// 特定の点の周辺だけ細かくする
var pointRestrictions = new[]
{
    new PointSizeRestriction(new Point3d(10, 0, 5), maxElementSize: 0.5),
    new PointSizeRestriction(new Point3d(-3, 2, 0), maxElementSize: 0.3),

    // 座標を直接指定することもできる
    new PointSizeRestriction(x: 0, y: 0, z: 0, maxElementSize: 1.0),
};

var tet = NetgenMesher.GenerateFromBrep(brep, mp,
    pointRestrictions: pointRestrictions);
```

### ボックスによる制約

```csharp
var mp = MeshingParameters.Medium();
mp.MaxElementSize = 10.0;

// 指定した直方体の内側だけ細かくする
var boxRestrictions = new[]
{
    // BoundingBox で指定
    new BoxSizeRestriction(
        new BoundingBox(new Point3d(-5, -5, 0), new Point3d(5, 5, 10)),
        maxElementSize: 1.0),

    // 2点で指定（簡易コンストラクタ）
    new BoxSizeRestriction(
        min: new Point3d(20, 0, 0),
        max: new Point3d(30, 10, 5),
        maxElementSize: 0.5),
};

var tet = NetgenMesher.GenerateFromBrep(brep, mp,
    boxRestrictions: boxRestrictions);
```

### 点とボックスを組み合わせる

```csharp
var tet = NetgenMesher.GenerateFromBrep(
    brep, mp,
    pointRestrictions: pointRestrictions,
    boxRestrictions:   boxRestrictions);
```

---

## 5. スムージング

### netgen 組み込み最適化

netgen はメッシュ生成後に自動的に最適化（節点移動・辺スワップ）を行います。
ステップ数を増やすと品質が向上しますが、生成時間も増えます。

```csharp
var mp = MeshingParameters.Medium();

// 組み込み最適化の有効/無効
mp.EnableSurfaceOptimization = true;  // 表面メッシュの最適化（デフォルト: true）
mp.EnableVolumeOptimization  = true;  // 体積メッシュの最適化（デフォルト: true）

// 最適化ステップ数（デフォルト: 3）
mp.OptimizationSteps2D = 5;  // 表面メッシュの最適化回数
mp.OptimizationSteps3D = 5;  // 体積メッシュの最適化回数

var tet = NetgenMesher.GenerateFromBrep(brep, mp);
```

### ラプラシアンスムージング（後処理）

生成後に内部頂点を近傍頂点の重心方向へ移動させます。
**境界頂点は固定**されるため、外形は保たれます。

```csharp
var mp = MeshingParameters.Medium();

// 生成後に自動でスムージングを適用
mp.LaplacianSmoothingIterations = 5;   // 繰り返し回数（0 = 無効、3〜10 が一般的）
mp.LaplacianSmoothingFactor     = 0.5; // 移動量 λ ∈ (0, 1]

var tet = NetgenMesher.GenerateFromBrep(brep, mp);
```

#### 移動量ファクター λ の目安

| λ の値 | 効果 | 推奨シーン |
|---|---|---|
| `0.1〜0.3` | 保守的・変形しにくい | 複雑な凹凸形状 |
| `0.5` | バランス型（デフォルト） | ほとんどのケース |
| `1.0` | 1ステップで重心へ移動 | 単純な凸形状・少ない繰り返しで済む |

#### `CreateSmoothed()` で手動適用

すでに生成済みのメッシュに後からスムージングをかけることもできます。

```csharp
// 生成
TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp);

// スムージングを手動で適用（元のメッシュは変更されない）
TetrahedralMesh smoothed = tet.CreateSmoothed(iterations: 10, factor: 0.3);

// 異なるパラメータで比較も可能
TetrahedralMesh smoothed2 = tet.CreateSmoothed(iterations: 3, factor: 1.0);
```

---

## 6. 結果の取得・利用

### 要素数・頂点数の確認

```csharp
TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp);

RhinoApp.WriteLine($"頂点数    : {tet.VertexCount}");
RhinoApp.WriteLine($"四面体数  : {tet.TetCount}");
```

### 個別要素へのアクセス

```csharp
// 頂点座標を Point3d で取得（0-based）
Point3d pt = tet.GetVertex(42);

// 四面体の頂点インデックスを取得（0-based）
var (a, b, c, d) = tet.GetTetrahedron(0);
Point3d v0 = tet.GetVertex(a);
Point3d v1 = tet.GetVertex(b);
Point3d v2 = tet.GetVertex(c);
Point3d v3 = tet.GetVertex(d);
```

### フラット配列として取得（FEM ソルバー連携等）

```csharp
// 頂点座標: [x0, y0, z0, x1, y1, z1, …]  長さ = VertexCount × 3
double[] vertices = tet.Vertices;

// 四面体インデックス: [a0, b0, c0, d0, a1, b1, c1, d1, …]  長さ = TetCount × 4
// インデックスは 0-based
int[] tetrahedra = tet.Tetrahedra;
```

### Rhino の表面メッシュとして表示する

四面体メッシュの外面（境界三角形）を Rhino の `Mesh` に変換して表示できます。

```csharp
Mesh surface = tet.ToRhinoSurfaceMesh();
doc.Objects.AddMesh(surface);
doc.Views.Redraw();
```

---

## 7. パラメータ一覧

### MeshingParameters

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `MaxElementSize` | `double` | `1e6` | 最大要素サイズ（無制限） |
| `MinElementSize` | `double` | `0` | 最小要素サイズ |
| `Fineness` | `double` | `0.5` | メッシュ密度 `[0=粗, 1=細]` |
| `Grading` | `double` | `0.3` | グレーディング `[0=均一, 1=積極]` |
| `ElementsPerEdge` | `double` | `2.0` | エッジあたりの要素数 |
| `ElementsPerCurve` | `double` | `2.0` | 曲率半径あたりの要素数 |
| `CloseEdgeRefinement` | `bool` | `false` | 近接エッジの細分化 |
| `CloseEdgeFactor` | `double` | `2.0` | 近接エッジ細分化係数 |
| `EnforceMinEdgeLength` | `bool` | `false` | 最小エッジ長の強制 |
| `MinEdgeLength` | `double` | `1e-4` | 強制する最小エッジ長 |
| `EnableSurfaceOptimization` | `bool` | `true` | 表面メッシュ最適化 |
| `EnableVolumeOptimization` | `bool` | `true` | 体積メッシュ最適化 |
| `OptimizationSteps2D` | `int` | `3` | 表面最適化ステップ数 |
| `OptimizationSteps3D` | `int` | `3` | 体積最適化ステップ数 |
| `LaplacianSmoothingIterations` | `int` | `0` | ラプラシアンスムージング繰り返し数 |
| `LaplacianSmoothingFactor` | `double` | `0.5` | スムージング移動量 λ `(0, 1]` |

### NetgenMesher のメソッド

| メソッド | 説明 |
|---|---|
| `Initialize()` | netgen カーネルを初期化（アプリ起動時に一度） |
| `Shutdown()` | netgen カーネルを終了（アプリ終了時） |
| `GenerateFromBrep(brep, mp, rhinoMp, pointRestrictions, boxRestrictions)` | Brep から四面体メッシュを生成 |
| `GenerateFromMesh(mesh, mp, pointRestrictions, boxRestrictions)` | 閉じた Mesh から四面体メッシュを生成 |

### TetrahedralMesh のメソッド

| メソッド / プロパティ | 説明 |
|---|---|
| `VertexCount` | 頂点数 |
| `TetCount` | 四面体要素数 |
| `Vertices` | 頂点座標フラット配列 `[x,y,z, …]` |
| `Tetrahedra` | 四面体インデックスフラット配列 `[a,b,c,d, …]` (0-based) |
| `GetVertex(index)` | 指定頂点を `Point3d` で取得 |
| `GetTetrahedron(index)` | 指定四面体の頂点インデックスを取得 |
| `ToRhinoSurfaceMesh()` | 外表面を Rhino `Mesh` に変換 |
| `CreateSmoothed(iterations, factor)` | ラプラシアンスムージングを適用した新しいメッシュを返す |

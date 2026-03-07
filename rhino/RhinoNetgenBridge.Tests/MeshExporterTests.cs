using System;
using System.IO;
using System.Linq;
using Xunit;

namespace RhinoNetgenBridge.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MeshExporter"/>.
    ///
    /// Each test writes to a temp file, reads it back as plain text, and
    /// asserts structural invariants (element-type keywords, node/element
    /// counts, 1-based indices, etc.).  The temp file is always deleted in a
    /// finally block so the file system is left clean regardless of outcome.
    /// </summary>
    public sealed class MeshExporterTests
    {
        // ---------------------------------------------------------------
        // Test mesh factories
        // ---------------------------------------------------------------

        /// <summary>
        /// Single TET4 element: 4 distinct vertices, 1 element.
        /// Vertices are arranged as a non-degenerate tetrahedron.
        /// </summary>
        private static TetrahedralMesh MakeSingleTet4()
        {
            double[] verts =
            {
                0.0, 0.0, 0.0,   // 0
                1.0, 0.0, 0.0,   // 1
                0.5, 1.0, 0.0,   // 2
                0.5, 0.5, 1.0,   // 3
            };
            int[] tets = { 0, 1, 2, 3 };
            return new TetrahedralMesh(verts, tets, 4);
        }

        /// <summary>
        /// Two TET4 elements sharing a face: 5 vertices, 2 elements.
        /// Used to verify multi-element output (indices, ordering).
        /// </summary>
        private static TetrahedralMesh MakeTwoTet4()
        {
            double[] verts =
            {
                0.0, 0.0, 0.0,   // 0
                1.0, 0.0, 0.0,   // 1
                0.5, 1.0, 0.0,   // 2
                0.5, 0.5, 1.0,   // 3
                0.5, 0.5, -1.0,  // 4
            };
            int[] tets = { 0, 1, 2, 3,   0, 2, 1, 4 };
            return new TetrahedralMesh(verts, tets, 4);
        }

        /// <summary>
        /// Single TET10 element: 4 corner nodes + 6 mid-edge nodes = 10 nodes.
        /// </summary>
        private static TetrahedralMesh MakeSingleTet10()
        {
            // Corners: v0=(0,0,0) v1=(2,0,0) v2=(1,2,0) v3=(1,1,2)
            // Mid-edge nodes (one per edge): m01 m02 m03 m12 m13 m23
            double[] verts =
            {
                0.0, 0.0, 0.0,   // 0 corner
                2.0, 0.0, 0.0,   // 1 corner
                1.0, 2.0, 0.0,   // 2 corner
                1.0, 1.0, 2.0,   // 3 corner
                1.0, 0.0, 0.0,   // 4 mid(0-1)
                0.5, 1.0, 0.0,   // 5 mid(0-2)
                0.5, 0.5, 1.0,   // 6 mid(0-3)
                1.5, 1.0, 0.0,   // 7 mid(1-2)
                1.5, 0.5, 1.0,   // 8 mid(1-3)
                1.0, 1.5, 1.0,   // 9 mid(2-3)
            };
            int[] tets = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            return new TetrahedralMesh(verts, tets, 10);
        }

        // ---------------------------------------------------------------
        // WriteAbaqus
        // ---------------------------------------------------------------

        [Fact]
        public void WriteAbaqus_Tet4_ContainsC3D4Keyword()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteAbaqus(MakeSingleTet4(), path);
                string content = File.ReadAllText(path);
                Assert.Contains("C3D4", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteAbaqus_Tet10_ContainsC3D10Keyword()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteAbaqus(MakeSingleTet10(), path);
                string content = File.ReadAllText(path);
                Assert.Contains("C3D10", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteAbaqus_NodeLines_AreOneBased()
        {
            // The first node line must start with "1,"
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteAbaqus(MakeSingleTet4(), path);
                string[] lines = File.ReadAllLines(path);
                int nodeSection = Array.FindIndex(lines, l => l.TrimStart().StartsWith("*Node"));
                Assert.True(nodeSection >= 0, "*Node keyword not found");
                string firstNodeLine = lines[nodeSection + 1];
                Assert.StartsWith("1,", firstNodeLine.TrimStart());
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteAbaqus_ElementLines_AreOneBased()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteAbaqus(MakeSingleTet4(), path);
                string[] lines = File.ReadAllLines(path);
                int elemSection = Array.FindIndex(lines,
                    l => l.TrimStart().StartsWith("*Element,"));
                Assert.True(elemSection >= 0, "*Element keyword not found");
                string firstElemLine = lines[elemSection + 1];
                Assert.StartsWith("1,", firstElemLine.TrimStart());
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteAbaqus_CustomPartName_AppearsInFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteAbaqus(MakeSingleTet4(), path, partName: "MyPart");
                string content = File.ReadAllText(path);
                Assert.Contains("MyPart", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteAbaqus_TwoElements_ElementCountInElset()
        {
            // *Elset,…,generate / 1, 2, 1  → "2" must appear on the generate line
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteAbaqus(MakeTwoTet4(), path);
                string[] lines = File.ReadAllLines(path);
                int elsetLine = Array.FindIndex(lines,
                    l => l.TrimStart().StartsWith("*Elset"));
                Assert.True(elsetLine >= 0);
                string generateLine = lines[elsetLine + 1];
                Assert.Contains("2", generateLine);
            }
            finally { File.Delete(path); }
        }

        // ---------------------------------------------------------------
        // WriteVtk
        // ---------------------------------------------------------------

        [Fact]
        public void WriteVtk_Tet4_CellType10()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteVtk(MakeSingleTet4(), path);
                string content = File.ReadAllText(path);
                // types DataArray must contain " 10"
                Assert.Contains(" 10", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteVtk_Tet10_CellType24()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteVtk(MakeSingleTet10(), path);
                string content = File.ReadAllText(path);
                Assert.Contains(" 24", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteVtk_NumberOfPoints_MatchesVertexCount()
        {
            var mesh = MakeTwoTet4(); // 5 vertices
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteVtk(mesh, path);
                string content = File.ReadAllText(path);
                Assert.Contains($"NumberOfPoints=\"{mesh.VertexCount}\"", content);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteVtk_NumberOfCells_MatchesTetCount()
        {
            var mesh = MakeTwoTet4(); // 2 elements
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteVtk(mesh, path);
                string content = File.ReadAllText(path);
                Assert.Contains($"NumberOfCells=\"{mesh.TetCount}\"", content);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteVtk_ConnectivityIndices_AreZeroBased()
        {
            // First element: nodes 0,1,2,3 → connectivity first line is "0 1 2 3"
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteVtk(MakeSingleTet4(), path);
                string content = File.ReadAllText(path);
                Assert.Contains("0 1 2 3", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        // ---------------------------------------------------------------
        // WriteGmsh
        // ---------------------------------------------------------------

        [Fact]
        public void WriteGmsh_Tet4_ElementType4()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteGmsh(MakeSingleTet4(), path);
                string[] lines = File.ReadAllLines(path);
                // Element lines have format: elm-id 4 2 1 1 n0 n1 n2 n3
                int elemLine = Array.FindIndex(lines, l => l.StartsWith("$Elements"))
                               + 2; // skip $Elements and count line
                string[] parts = lines[elemLine].Split(' ');
                Assert.Equal("4", parts[1]); // element type = 4 for TET4
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteGmsh_Tet10_ElementType11()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteGmsh(MakeSingleTet10(), path);
                string[] lines = File.ReadAllLines(path);
                int elemLine = Array.FindIndex(lines, l => l.StartsWith("$Elements"))
                               + 2;
                string[] parts = lines[elemLine].Split(' ');
                Assert.Equal("11", parts[1]); // element type = 11 for TET10
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteGmsh_NodeSection_CountMatchesVertexCount()
        {
            var mesh = MakeTwoTet4(); // 5 vertices
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteGmsh(mesh, path);
                string[] lines = File.ReadAllLines(path);
                int nodesHeader = Array.FindIndex(lines, l => l == "$Nodes");
                Assert.True(nodesHeader >= 0);
                Assert.Equal(mesh.VertexCount.ToString(), lines[nodesHeader + 1]);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteGmsh_NodeIndices_AreOneBased()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteGmsh(MakeSingleTet4(), path);
                string[] lines = File.ReadAllLines(path);
                int nodesHeader = Array.FindIndex(lines, l => l == "$Nodes");
                string firstNodeLine = lines[nodesHeader + 2]; // after $Nodes + count
                Assert.StartsWith("1 ", firstNodeLine);
            }
            finally { File.Delete(path); }
        }

        // ---------------------------------------------------------------
        // WriteNastran
        // ---------------------------------------------------------------

        [Fact]
        public void WriteNastran_Tet4_ContainsCtetraCard()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteNastran(MakeSingleTet4(), path);
                string content = File.ReadAllText(path);
                Assert.Contains("CTETRA", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteNastran_Tet4_CtetraHasFourCornerNodes()
        {
            // TET4 CTETRA line: CTETRA,eid,pid,G1,G2,G3,G4 → 7 comma-separated fields
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteNastran(MakeSingleTet4(), path);
                string[] lines = File.ReadAllLines(path);
                string? ctetraLine = lines.FirstOrDefault(l =>
                    l.StartsWith("CTETRA,", StringComparison.Ordinal));
                Assert.NotNull(ctetraLine);
                Assert.Equal(7, ctetraLine!.Split(',').Length); // CTETRA,EID,PID,G1,G2,G3,G4
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteNastran_Tet10_HasContinuationCard()
        {
            // TET10 requires a '+' continuation card for mid-side nodes
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteNastran(MakeSingleTet10(), path);
                string content = File.ReadAllText(path);
                Assert.Contains("\n+,", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteNastran_GridLines_AreOneBased()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteNastran(MakeSingleTet4(), path);
                string[] lines = File.ReadAllLines(path);
                string? firstGrid = lines.FirstOrDefault(l =>
                    l.StartsWith("GRID,", StringComparison.Ordinal));
                Assert.NotNull(firstGrid);
                // GRID,1,,...
                Assert.StartsWith("GRID,1,", firstGrid);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void WriteNastran_ContainsEndData()
        {
            string path = Path.GetTempFileName();
            try
            {
                MeshExporter.WriteNastran(MakeSingleTet4(), path);
                string content = File.ReadAllText(path);
                Assert.Contains("ENDDATA", content, StringComparison.Ordinal);
            }
            finally { File.Delete(path); }
        }

        // ---------------------------------------------------------------
        // ArgumentNullException guards (all four writers)
        // ---------------------------------------------------------------

        [Fact]
        public void WriteAbaqus_NullMesh_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteAbaqus(null!, "out.inp"));

        [Fact]
        public void WriteAbaqus_NullPath_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteAbaqus(MakeSingleTet4(), null!));

        [Fact]
        public void WriteVtk_NullMesh_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteVtk(null!, "out.vtu"));

        [Fact]
        public void WriteVtk_NullPath_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteVtk(MakeSingleTet4(), null!));

        [Fact]
        public void WriteGmsh_NullMesh_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteGmsh(null!, "out.msh"));

        [Fact]
        public void WriteGmsh_NullPath_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteGmsh(MakeSingleTet4(), null!));

        [Fact]
        public void WriteNastran_NullMesh_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteNastran(null!, "out.bdf"));

        [Fact]
        public void WriteNastran_NullPath_ThrowsArgumentNullException() =>
            Assert.Throws<ArgumentNullException>(() =>
                MeshExporter.WriteNastran(MakeSingleTet4(), null!));
    }
}

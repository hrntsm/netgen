using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// Export a <see cref="TetrahedralMesh"/> to common FEM / visualisation
    /// file formats.
    ///
    /// <para>Supported formats:</para>
    /// <list type="table">
    ///   <listheader><term>Method</term><description>Format</description></listheader>
    ///   <item><term><see cref="WriteAbaqus"/></term>
    ///         <description>Abaqus .inp – for Abaqus / CalculiX solvers</description></item>
    ///   <item><term><see cref="WriteVtk"/></term>
    ///         <description>VTK XML Unstructured Grid .vtu – for ParaView / OpenFOAM</description></item>
    ///   <item><term><see cref="WriteGmsh"/></term>
    ///         <description>Gmsh MSH v2 .msh – for Gmsh / FEniCS / Elmer</description></item>
    ///   <item><term><see cref="WriteNastran"/></term>
    ///         <description>NASTRAN free-field bulk-data .bdf – for NASTRAN / MSC Nastran</description></item>
    /// </list>
    ///
    /// <para>Both TET4 (linear) and TET10 (second-order) meshes are supported.
    /// The element type written is determined by
    /// <see cref="TetrahedralMesh.NodesPerElement"/>.</para>
    ///
    /// <para>Example:</para>
    /// <code>
    ///   TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp);
    ///
    ///   MeshExporter.WriteAbaqus(tet, "model.inp");
    ///   MeshExporter.WriteVtk   (tet, "model.vtu");
    ///   MeshExporter.WriteGmsh  (tet, "model.msh");
    ///   MeshExporter.WriteNastran(tet, "model.bdf");
    /// </code>
    /// </summary>
    public static class MeshExporter
    {
        // ---------------------------------------------------------------
        // Abaqus .inp
        // ---------------------------------------------------------------

        /// <summary>
        /// Write an Abaqus input file (.inp).
        ///
        /// <para>Element types used:</para>
        /// <list type="bullet">
        ///   <item><description>TET4  → <c>C3D4</c></description></item>
        ///   <item><description>TET10 → <c>C3D10</c></description></item>
        /// </list>
        /// Node and element numbering is 1-based.
        /// </summary>
        /// <param name="mesh">The tetrahedral mesh to export.</param>
        /// <param name="path">Output file path (e.g. "model.inp").</param>
        /// <param name="partName">
        ///   Optional name written to the <c>*Part</c> keyword.
        ///   Defaults to <c>"Part-1"</c>.
        /// </param>
        public static void WriteAbaqus(TetrahedralMesh mesh, string path,
                                        string partName = "Part-1")
        {
            ValidateArgs(mesh, path);

            string elementType = mesh.NodesPerElement == 10 ? "C3D10" : "C3D4";

            using var sw = new StreamWriter(path, append: false, Encoding.ASCII);

            // Header
            sw.WriteLine("*Heading");
            sw.WriteLine($"** Exported by RhinoNetgenBridge  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine("**");
            sw.WriteLine($"*Part, name={partName}");

            // Nodes (1-based)
            sw.WriteLine("*Node");
            int nv = mesh.VertexCount;
            for (int i = 0; i < nv; ++i)
            {
                var p = mesh.GetVertex(i);
                sw.WriteLine(
                    $"{i + 1}, {F(p.X)}, {F(p.Y)}, {F(p.Z)}");
            }

            // Elements (1-based, 1-based node references)
            sw.WriteLine($"*Element, type={elementType}");
            int ne  = mesh.TetCount;
            int npe = mesh.NodesPerElement;
            var sb  = new StringBuilder();
            for (int i = 0; i < ne; ++i)
            {
                sb.Clear();
                sb.Append(i + 1);
                int base_ = i * npe;
                for (int k = 0; k < npe; ++k)
                {
                    sb.Append(", ");
                    sb.Append(mesh.Tetrahedra[base_ + k] + 1);
                }
                sw.WriteLine(sb);
            }

            // Elset and Nset for the whole mesh
            sw.WriteLine($"*Elset, elset=All, generate");
            sw.WriteLine($"1, {ne}, 1");
            sw.WriteLine($"*Nset, nset=All, generate");
            sw.WriteLine($"1, {nv}, 1");

            sw.WriteLine("*End Part");
            sw.WriteLine("**");
            sw.WriteLine("** Add *Assembly, *Step, boundary conditions, etc. below.");
        }

        // ---------------------------------------------------------------
        // VTK XML Unstructured Grid .vtu
        // ---------------------------------------------------------------

        /// <summary>
        /// Write a VTK XML Unstructured Grid file (.vtu).
        ///
        /// <para>Cell types used:</para>
        /// <list type="bullet">
        ///   <item><description>TET4  → VTK_TETRA (type 10)</description></item>
        ///   <item><description>TET10 → VTK_QUADRATIC_TETRA (type 24)</description></item>
        /// </list>
        /// The file uses ASCII encoding and 0-based connectivity.
        /// </summary>
        /// <param name="mesh">The tetrahedral mesh to export.</param>
        /// <param name="path">Output file path (e.g. "model.vtu").</param>
        public static void WriteVtk(TetrahedralMesh mesh, string path)
        {
            ValidateArgs(mesh, path);

            int nv     = mesh.VertexCount;
            int ne     = mesh.TetCount;
            int npe    = mesh.NodesPerElement;
            int vtkType = npe == 10 ? 24 : 10;

            using var sw = new StreamWriter(path, append: false, Encoding.ASCII);

            sw.WriteLine("<?xml version=\"1.0\"?>");
            sw.WriteLine("<!-- Exported by RhinoNetgenBridge "
                         + $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} -->");
            sw.WriteLine("<VTKFile type=\"UnstructuredGrid\" version=\"0.1\" "
                         + "byte_order=\"LittleEndian\">");
            sw.WriteLine("  <UnstructuredGrid>");
            sw.WriteLine($"    <Piece NumberOfPoints=\"{nv}\" NumberOfCells=\"{ne}\">");

            // Points
            sw.WriteLine("      <Points>");
            sw.WriteLine("        <DataArray type=\"Float64\" "
                         + "NumberOfComponents=\"3\" format=\"ascii\">");
            for (int i = 0; i < nv; ++i)
            {
                var p = mesh.GetVertex(i);
                sw.WriteLine($"          {F(p.X)} {F(p.Y)} {F(p.Z)}");
            }
            sw.WriteLine("        </DataArray>");
            sw.WriteLine("      </Points>");

            // Cells
            sw.WriteLine("      <Cells>");

            // connectivity
            sw.WriteLine("        <DataArray type=\"Int32\" "
                         + "Name=\"connectivity\" format=\"ascii\">");
            for (int i = 0; i < ne; ++i)
            {
                int base_ = i * npe;
                sw.Write("          ");
                for (int k = 0; k < npe; ++k)
                {
                    if (k > 0) sw.Write(' ');
                    sw.Write(mesh.Tetrahedra[base_ + k]);
                }
                sw.WriteLine();
            }
            sw.WriteLine("        </DataArray>");

            // offsets
            sw.WriteLine("        <DataArray type=\"Int32\" "
                         + "Name=\"offsets\" format=\"ascii\">");
            sw.Write("         ");
            for (int i = 1; i <= ne; ++i)
                sw.Write($" {i * npe}");
            sw.WriteLine();
            sw.WriteLine("        </DataArray>");

            // types
            sw.WriteLine("        <DataArray type=\"UInt8\" "
                         + "Name=\"types\" format=\"ascii\">");
            sw.Write("         ");
            for (int i = 0; i < ne; ++i)
                sw.Write($" {vtkType}");
            sw.WriteLine();
            sw.WriteLine("        </DataArray>");

            sw.WriteLine("      </Cells>");
            sw.WriteLine("    </Piece>");
            sw.WriteLine("  </UnstructuredGrid>");
            sw.WriteLine("</VTKFile>");
        }

        // ---------------------------------------------------------------
        // Gmsh MSH v2 .msh
        // ---------------------------------------------------------------

        /// <summary>
        /// Write a Gmsh MSH version 2 file (.msh).
        ///
        /// <para>Element types used:</para>
        /// <list type="bullet">
        ///   <item><description>TET4  → Gmsh type 4 (4-node tetrahedron)</description></item>
        ///   <item><description>TET10 → Gmsh type 11 (10-node tetrahedron)</description></item>
        /// </list>
        /// Node and element numbering is 1-based.
        /// All elements are placed in physical group 1.
        /// </summary>
        /// <param name="mesh">The tetrahedral mesh to export.</param>
        /// <param name="path">Output file path (e.g. "model.msh").</param>
        public static void WriteGmsh(TetrahedralMesh mesh, string path)
        {
            ValidateArgs(mesh, path);

            int nv    = mesh.VertexCount;
            int ne    = mesh.TetCount;
            int npe   = mesh.NodesPerElement;
            int gmshType = npe == 10 ? 11 : 4;

            using var sw = new StreamWriter(path, append: false, Encoding.ASCII);

            // MeshFormat
            sw.WriteLine("$MeshFormat");
            sw.WriteLine("2.2 0 8");
            sw.WriteLine("$EndMeshFormat");

            // PhysicalNames (optional but useful)
            sw.WriteLine("$PhysicalNames");
            sw.WriteLine("1");
            sw.WriteLine("3 1 \"Volume\"");
            sw.WriteLine("$EndPhysicalNames");

            // Nodes (1-based)
            sw.WriteLine("$Nodes");
            sw.WriteLine(nv);
            for (int i = 0; i < nv; ++i)
            {
                var p = mesh.GetVertex(i);
                sw.WriteLine($"{i + 1} {F(p.X)} {F(p.Y)} {F(p.Z)}");
            }
            sw.WriteLine("$EndNodes");

            // Elements (1-based, 1-based node refs)
            // Format: elm-number elm-type num-tags tag1 [tag2 …] node-list
            // We use 2 tags: physical group = 1, elementary entity = 1.
            sw.WriteLine("$Elements");
            sw.WriteLine(ne);
            var sb = new StringBuilder();
            for (int i = 0; i < ne; ++i)
            {
                sb.Clear();
                sb.Append(i + 1);
                sb.Append(' ');
                sb.Append(gmshType);
                sb.Append(" 2 1 1");
                int base_ = i * npe;
                for (int k = 0; k < npe; ++k)
                {
                    sb.Append(' ');
                    sb.Append(mesh.Tetrahedra[base_ + k] + 1);
                }
                sw.WriteLine(sb);
            }
            sw.WriteLine("$EndElements");
        }

        // ---------------------------------------------------------------
        // NASTRAN free-field bulk data .bdf
        // ---------------------------------------------------------------

        /// <summary>
        /// Write a NASTRAN free-field bulk data file (.bdf).
        ///
        /// <para>Element types used:</para>
        /// <list type="bullet">
        ///   <item><description>TET4  → <c>CTETRA</c> (4-node)</description></item>
        ///   <item><description>TET10 → <c>CTETRA</c> (10-node)</description></item>
        /// </list>
        /// Both variants use the same NASTRAN <c>CTETRA</c> entry; the
        /// 10-node variant simply lists the mid-side nodes in fields 6–10
        /// of a continuation card.
        /// Node and element IDs are 1-based.  All elements reference
        /// property ID 1 (<c>PSOLID,1,1</c>).
        /// </summary>
        /// <param name="mesh">The tetrahedral mesh to export.</param>
        /// <param name="path">Output file path (e.g. "model.bdf").</param>
        public static void WriteNastran(TetrahedralMesh mesh, string path)
        {
            ValidateArgs(mesh, path);

            int nv  = mesh.VertexCount;
            int ne  = mesh.TetCount;
            int npe = mesh.NodesPerElement;

            using var sw = new StreamWriter(path, append: false, Encoding.ASCII);

            sw.WriteLine("$ NASTRAN free-field bulk data");
            sw.WriteLine($"$ Exported by RhinoNetgenBridge  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine("$ Vertices: " + nv + "  Elements: " + ne);
            sw.WriteLine("BEGIN BULK");
            sw.WriteLine("$");

            // Material and property (placeholder – user must fill in)
            sw.WriteLine("$ Material and solid property – edit as required:");
            sw.WriteLine("MAT1,1,210000.0,,0.3");
            sw.WriteLine("PSOLID,1,1");
            sw.WriteLine("$");

            // GRID cards (1-based)
            for (int i = 0; i < nv; ++i)
            {
                var p = mesh.GetVertex(i);
                // GRID,ID,,X,Y,Z
                sw.WriteLine(
                    $"GRID,{i + 1},,{F(p.X)},{F(p.Y)},{F(p.Z)}");
            }

            sw.WriteLine("$");

            // CTETRA cards (1-based)
            int[] tets = mesh.Tetrahedra;
            for (int i = 0; i < ne; ++i)
            {
                int b = i * npe;
                if (npe == 4)
                {
                    // CTETRA,EID,PID,G1,G2,G3,G4
                    sw.WriteLine(
                        $"CTETRA,{i + 1},1," +
                        $"{tets[b]+1},{tets[b+1]+1},{tets[b+2]+1},{tets[b+3]+1}");
                }
                else // TET10
                {
                    // CTETRA,EID,PID,G1,G2,G3,G4,G5,G6,
                    //        +,G7,G8,G9,G10
                    sw.WriteLine(
                        $"CTETRA,{i + 1},1," +
                        $"{tets[b]+1},{tets[b+1]+1},{tets[b+2]+1},{tets[b+3]+1}," +
                        $"{tets[b+4]+1},{tets[b+5]+1},+");
                    sw.WriteLine(
                        $"+,{tets[b+6]+1},{tets[b+7]+1},{tets[b+8]+1},{tets[b+9]+1}");
                }
            }

            sw.WriteLine("$");
            sw.WriteLine("ENDDATA");
        }

        // ---------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------

        private static void ValidateArgs(TetrahedralMesh mesh, string path)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (path == null) throw new ArgumentNullException(nameof(path));
        }

        /// Format a double with enough precision for FEM coordinates.
        private static string F(double v) =>
            v.ToString("G15", CultureInfo.InvariantCulture);
    }
}

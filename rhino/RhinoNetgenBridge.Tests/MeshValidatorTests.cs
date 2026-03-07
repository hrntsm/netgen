using System;
using Rhino.Geometry;
using Xunit;

namespace RhinoNetgenBridge.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MeshValidator"/>.
    ///
    /// Each test builds a minimal Rhino surface mesh by hand so that the
    /// expected outcome is deterministic and does not require a running Rhino
    /// process (basic Mesh geometry operations work with the RhinoCommon NuGet
    /// package without a live Rhino host).
    /// </summary>
    public sealed class MeshValidatorTests
    {
        // ---------------------------------------------------------------
        // Test mesh factories
        // ---------------------------------------------------------------

        /// <summary>
        /// Closed tetrahedron: 4 vertices, 4 triangular faces, every edge
        /// shared by exactly 2 faces.
        /// </summary>
        private static Mesh MakeClosedTetrahedron()
        {
            var m = new Mesh();
            m.Vertices.Add(0.0,  0.0,  0.0); // 0
            m.Vertices.Add(1.0,  0.0,  0.0); // 1
            m.Vertices.Add(0.5,  1.0,  0.0); // 2
            m.Vertices.Add(0.5,  0.5,  1.0); // 3

            // Four outward-facing triangles
            m.Faces.AddFace(0, 2, 1); // bottom
            m.Faces.AddFace(0, 1, 3); // front
            m.Faces.AddFace(1, 2, 3); // right
            m.Faces.AddFace(0, 3, 2); // left
            return m;
        }

        /// <summary>
        /// Same tetrahedron with one face omitted → 3 naked edges.
        /// </summary>
        private static Mesh MakeOpenTetrahedron()
        {
            var m = new Mesh();
            m.Vertices.Add(0.0,  0.0,  0.0);
            m.Vertices.Add(1.0,  0.0,  0.0);
            m.Vertices.Add(0.5,  1.0,  0.0);
            m.Vertices.Add(0.5,  0.5,  1.0);

            // Missing face (0, 3, 2) → edges (0,3), (3,2), (0,2) become naked
            m.Faces.AddFace(0, 2, 1);
            m.Faces.AddFace(0, 1, 3);
            m.Faces.AddFace(1, 2, 3);
            return m;
        }

        /// <summary>
        /// 5 vertices; edge (0,1) is shared by 3 faces → non-manifold.
        /// </summary>
        private static Mesh MakeNonManifoldMesh()
        {
            var m = new Mesh();
            m.Vertices.Add(0.0,  0.0,  0.0); // 0
            m.Vertices.Add(1.0,  0.0,  0.0); // 1
            m.Vertices.Add(0.5,  1.0,  0.0); // 2
            m.Vertices.Add(0.5,  0.5,  1.0); // 3
            m.Vertices.Add(0.5, -1.0,  0.5); // 4  ← extra vertex

            m.Faces.AddFace(0, 2, 1); // edge (0,1) used – face 1
            m.Faces.AddFace(0, 1, 3); // edge (0,1) used – face 2
            m.Faces.AddFace(0, 1, 4); // edge (0,1) used – face 3 → non-manifold!
            m.Faces.AddFace(1, 2, 3);
            m.Faces.AddFace(0, 3, 2);
            return m;
        }

        /// <summary>
        /// Triangle with three collinear vertices → zero-area (degenerate) face.
        /// The surrounding closed shell is omitted so we only count the one bad face.
        /// </summary>
        private static Mesh MakeMeshWithDegenerateFace()
        {
            var m = new Mesh();
            // All three vertices on the X axis – zero area
            m.Vertices.Add(0.0, 0.0, 0.0); // 0
            m.Vertices.Add(1.0, 0.0, 0.0); // 1
            m.Vertices.Add(2.0, 0.0, 0.0); // 2

            m.Faces.AddFace(0, 1, 2);
            return m;
        }

        /// <summary>
        /// A single triangle plus a vertex that is not used by any face.
        /// </summary>
        private static Mesh MakeMeshWithUnusedVertex()
        {
            var m = new Mesh();
            m.Vertices.Add(0.0, 0.0, 0.0); // 0 – used
            m.Vertices.Add(1.0, 0.0, 0.0); // 1 – used
            m.Vertices.Add(0.5, 1.0, 0.0); // 2 – used
            m.Vertices.Add(9.0, 9.0, 9.0); // 3 – NOT used

            m.Faces.AddFace(0, 1, 2);
            return m;
        }

        // ---------------------------------------------------------------
        // IsValid / IsClosed / IsManifold
        // ---------------------------------------------------------------

        [Fact]
        public void Validate_ClosedTetrahedron_IsValid()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeClosedTetrahedron());

            Assert.True(result.IsValid);
            Assert.True(result.IsClosed);
            Assert.True(result.IsManifold);
            Assert.Equal(0, result.NakedEdgeCount);
            Assert.Equal(0, result.NonManifoldEdgeCount);
            Assert.Equal(0, result.DegenerateFaceCount);
        }

        [Fact]
        public void Validate_OpenTetrahedron_HasNakedEdges()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeOpenTetrahedron());

            Assert.False(result.IsValid);
            Assert.False(result.IsClosed);
            Assert.True(result.NakedEdgeCount > 0);
        }

        [Fact]
        public void Validate_NonManifoldMesh_HasNonManifoldEdge()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeNonManifoldMesh());

            Assert.False(result.IsValid);
            Assert.False(result.IsManifold);
            Assert.Equal(1, result.NonManifoldEdgeCount);
        }

        [Fact]
        public void Validate_DegenerateFace_Detected()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeMeshWithDegenerateFace());

            Assert.False(result.IsValid);
            Assert.Equal(1, result.DegenerateFaceCount);
        }

        [Fact]
        public void Validate_UnusedVertex_Detected()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeMeshWithUnusedVertex());

            // Unused vertex doesn't block IsValid, but it is reported
            Assert.Equal(1, result.UnusedVertexCount);
            Assert.True(result.Issues.Count >= 1);
        }

        // ---------------------------------------------------------------
        // Issues list
        // ---------------------------------------------------------------

        [Fact]
        public void Validate_NakedEdgeIssues_ContainCorrectType()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeOpenTetrahedron());

            foreach (var issue in result.Issues)
            {
                // All non-truncation issues for an open mesh should be NakedEdge
                if (!issue.Description.Contains("truncated"))
                    Assert.Equal(MeshIssueType.NakedEdge, issue.Type);
            }
        }

        [Fact]
        public void Validate_NonManifoldIssue_HasCorrectType()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeNonManifoldMesh());

            bool found = false;
            foreach (var issue in result.Issues)
            {
                if (issue.Type == MeshIssueType.NonManifoldEdge)
                {
                    found = true;
                    Assert.Contains("non-manifold", issue.Description,
                        StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }
            Assert.True(found, "Expected at least one NonManifoldEdge issue.");
        }

        [Fact]
        public void Validate_DegenerateFaceIssue_CarriesFaceIndex()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeMeshWithDegenerateFace());

            Assert.Equal(1, result.Issues.Count);
            Assert.Equal(MeshIssueType.DegenerateFace, result.Issues[0].Type);
            Assert.Equal(0, result.Issues[0].Index); // first (only) face
        }

        // ---------------------------------------------------------------
        // ArgumentNullException
        // ---------------------------------------------------------------

        [Fact]
        public void Validate_NullMesh_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => MeshValidator.Validate(null!));
        }

        // ---------------------------------------------------------------
        // MeshValidationResult.ToString
        // ---------------------------------------------------------------

        [Fact]
        public void ToString_ValidMesh_ContainsValid()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeClosedTetrahedron());
            string s = result.ToString();

            Assert.Contains("Valid", s, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Invalid", s, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToString_InvalidMesh_ContainsNakedEdgeCount()
        {
            MeshValidationResult result = MeshValidator.Validate(MakeOpenTetrahedron());
            string s = result.ToString();

            Assert.Contains("Invalid", s, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(result.NakedEdgeCount.ToString(), s);
        }

        // ---------------------------------------------------------------
        // MeshIssue.ToString
        // ---------------------------------------------------------------

        [Fact]
        public void MeshIssue_ToString_IncludesTypeAndDescription()
        {
            var issue = new MeshIssue(MeshIssueType.NakedEdge, "Edge (0, 1) is not shared.");
            string s = issue.ToString();

            Assert.Contains("NakedEdge", s);
            Assert.Contains("Edge (0, 1)", s);
        }

        [Fact]
        public void MeshIssue_ToString_WithIndex_IncludesIndex()
        {
            var issue = new MeshIssue(MeshIssueType.DegenerateFace, "Zero area.", 5);
            string s = issue.ToString();

            Assert.Contains("5", s);
        }

        [Fact]
        public void MeshIssue_ToString_WithoutIndex_NoIndexLabel()
        {
            var issue = new MeshIssue(MeshIssueType.NakedEdge, "Edge (2, 3) is not shared.");
            string s = issue.ToString();

            Assert.DoesNotContain("index", s, StringComparison.OrdinalIgnoreCase);
        }
    }
}

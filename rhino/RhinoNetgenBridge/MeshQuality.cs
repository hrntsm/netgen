using System;
using System.Collections.Generic;

namespace RhinoNetgenBridge
{
    // =========================================================================
    // Per-element quality record
    // =========================================================================

    /// <summary>
    /// Quality metrics for a single tetrahedral element.
    ///
    /// <para><b>Ideal values (regular equilateral tetrahedron):</b></para>
    /// <list type="table">
    ///   <listheader><term>Metric</term><description>Ideal value</description></listheader>
    ///   <item><term><see cref="MeanRatio"/></term>       <description>1.0</description></item>
    ///   <item><term><see cref="MinDihedralAngleDegrees"/></term><description>~70.53°</description></item>
    ///   <item><term><see cref="MaxDihedralAngleDegrees"/></term><description>~70.53°</description></item>
    ///   <item><term><see cref="EdgeLengthRatio"/></term> <description>1.0</description></item>
    /// </list>
    /// </summary>
    public readonly struct TetQuality
    {
        /// <summary>
        /// Normalised mean-ratio quality η ∈ (0, 1].
        ///
        /// <para>Computed as <c>η = 12 × (3V)^(2/3) / Σ(l_i²)</c> where V is
        /// the element volume and the sum runs over all six edge lengths.
        /// η = 1 for a perfect equilateral tetrahedron; η → 0 for a degenerate
        /// (flat or needle-shaped) element.</para>
        ///
        /// <para>Values below 0.2 typically indicate poor-quality elements that
        /// may cause problems in FEM analysis.</para>
        /// </summary>
        public double MeanRatio { get; }

        /// <summary>
        /// Minimum dihedral angle of this tetrahedron in degrees.
        ///
        /// A tetrahedron has six edges; the dihedral angle along each edge is the
        /// interior angle between its two adjacent faces.  The ideal value for a
        /// regular tetrahedron is ≈ 70.53°.  Very small angles (&lt; ~10°) indicate
        /// sliver or needle elements.
        /// </summary>
        public double MinDihedralAngleDegrees { get; }

        /// <summary>
        /// Maximum dihedral angle of this tetrahedron in degrees.
        /// The ideal value is ≈ 70.53°.  Very large angles (&gt; ~160°) indicate
        /// flat, cap-like elements.
        /// </summary>
        public double MaxDihedralAngleDegrees { get; }

        /// <summary>
        /// Signed volume of the tetrahedron (model units³).
        /// A negative value indicates an inverted element.
        /// </summary>
        public double Volume { get; }

        /// <summary>
        /// Ratio of the shortest edge to the longest edge.
        /// 1.0 = all edges equal (equilateral); values near 0 indicate needle
        /// elements.
        /// </summary>
        public double EdgeLengthRatio { get; }

        internal TetQuality(double meanRatio, double minDihedral, double maxDihedral,
                             double volume, double edgeLengthRatio)
        {
            MeanRatio               = meanRatio;
            MinDihedralAngleDegrees = minDihedral;
            MaxDihedralAngleDegrees = maxDihedral;
            Volume                  = volume;
            EdgeLengthRatio         = edgeLengthRatio;
        }

        /// <summary>
        /// Returns <c>true</c> if the element is inverted (negative volume).
        /// </summary>
        public bool IsInverted => Volume < 0;

        /// <inheritdoc/>
        public override string ToString() =>
            $"η={MeanRatio:F3}  DihedralMin={MinDihedralAngleDegrees:F1}°  " +
            $"DihedralMax={MaxDihedralAngleDegrees:F1}°  Vol={Volume:G4}  " +
            $"EdgeRatio={EdgeLengthRatio:F3}";
    }


    // =========================================================================
    // Mesh-wide quality statistics
    // =========================================================================

    /// <summary>
    /// Aggregate quality statistics computed over an entire
    /// <see cref="TetrahedralMesh"/>.
    ///
    /// <para>Obtain an instance via
    /// <see cref="TetrahedralMesh.ComputeQualityStatistics"/>.</para>
    /// </summary>
    public sealed class MeshQualityStatistics
    {
        // ----------------------------------------------------------------
        // Mean Ratio η
        // ----------------------------------------------------------------

        /// <summary>Minimum mean-ratio quality η across all elements.</summary>
        public double MinMeanRatio { get; }

        /// <summary>Maximum mean-ratio quality η across all elements.</summary>
        public double MaxMeanRatio { get; }

        /// <summary>Average mean-ratio quality η across all elements.</summary>
        public double AverageMeanRatio { get; }

        /// <summary>Standard deviation of mean-ratio quality.</summary>
        public double StdDevMeanRatio { get; }

        /// <summary>Index of the element with the lowest mean-ratio (worst quality).</summary>
        public int WorstElementIndex { get; }

        /// <summary>Index of the element with the highest mean-ratio (best quality).</summary>
        public int BestElementIndex { get; }

        // ----------------------------------------------------------------
        // Dihedral angles
        // ----------------------------------------------------------------

        /// <summary>Minimum dihedral angle over all elements and all edges (degrees).</summary>
        public double MinDihedralAngleDegrees { get; }

        /// <summary>Maximum dihedral angle over all elements and all edges (degrees).</summary>
        public double MaxDihedralAngleDegrees { get; }

        /// <summary>Average of minimum dihedral angles per element (degrees).</summary>
        public double AverageMinDihedralAngleDegrees { get; }

        // ----------------------------------------------------------------
        // Volume
        // ----------------------------------------------------------------

        /// <summary>Smallest element volume (model units³).</summary>
        public double MinVolume { get; }

        /// <summary>Largest element volume (model units³).</summary>
        public double MaxVolume { get; }

        /// <summary>Total mesh volume (model units³).</summary>
        public double TotalVolume { get; }

        // ----------------------------------------------------------------
        // Counts
        // ----------------------------------------------------------------

        /// <summary>Total number of elements in the mesh.</summary>
        public int TotalElements { get; }

        /// <summary>Number of inverted elements (negative volume).</summary>
        public int InvertedElements { get; }

        // ----------------------------------------------------------------
        // Constructor (internal – built by TetrahedralMesh)
        // ----------------------------------------------------------------

        internal MeshQualityStatistics(
            double minEta, double maxEta, double avgEta, double stdEta,
            int worstIdx, int bestIdx,
            double minDih, double maxDih, double avgMinDih,
            double minVol, double maxVol, double totalVol,
            int totalElements, int invertedElements)
        {
            MinMeanRatio     = minEta;
            MaxMeanRatio     = maxEta;
            AverageMeanRatio = avgEta;
            StdDevMeanRatio  = stdEta;
            WorstElementIndex = worstIdx;
            BestElementIndex  = bestIdx;
            MinDihedralAngleDegrees        = minDih;
            MaxDihedralAngleDegrees        = maxDih;
            AverageMinDihedralAngleDegrees = avgMinDih;
            MinVolume       = minVol;
            MaxVolume       = maxVol;
            TotalVolume     = totalVol;
            TotalElements   = totalElements;
            InvertedElements = invertedElements;
        }

        /// <summary>
        /// Count the number of elements whose mean-ratio η is below
        /// <paramref name="threshold"/>.
        ///
        /// <para>This requires iterating all elements on first call and is O(n).
        /// The result is not cached.</para>
        /// </summary>
        public int ElementsBelowQualityThreshold(double threshold,
            IReadOnlyList<TetQuality> perElementQualities)
        {
            int count = 0;
            foreach (var q in perElementQualities)
                if (q.MeanRatio < threshold) ++count;
            return count;
        }

        /// <inheritdoc/>
        public override string ToString() =>
            $"Elements: {TotalElements}  Inverted: {InvertedElements}\n" +
            $"MeanRatio η  min={MinMeanRatio:F3}  avg={AverageMeanRatio:F3}  " +
                $"max={MaxMeanRatio:F3}  σ={StdDevMeanRatio:F3}\n" +
            $"DihedralAngle  min={MinDihedralAngleDegrees:F1}°  " +
                $"avg(min)={AverageMinDihedralAngleDegrees:F1}°  " +
                $"max={MaxDihedralAngleDegrees:F1}°\n" +
            $"Volume  min={MinVolume:G4}  max={MaxVolume:G4}  total={TotalVolume:G6}";
    }


    // =========================================================================
    // Internal computation helpers  (no Rhino geometry dependency)
    // =========================================================================

    internal static class QualityComputer
    {
        private const double Rad2Deg = 180.0 / Math.PI;

        // ------------------------------------------------------------------
        // Public entry points
        // ------------------------------------------------------------------

        internal static TetQuality Compute(
            double[] verts, int i0, int i1, int i2, int i3)
        {
            // Vertex positions
            double ax = verts[i0*3], ay = verts[i0*3+1], az = verts[i0*3+2];
            double bx = verts[i1*3], by = verts[i1*3+1], bz = verts[i1*3+2];
            double cx = verts[i2*3], cy = verts[i2*3+1], cz = verts[i2*3+2];
            double dx = verts[i3*3], dy = verts[i3*3+1], dz = verts[i3*3+2];

            // Edge vectors from a
            double e1x = bx-ax, e1y = by-ay, e1z = bz-az;
            double e2x = cx-ax, e2y = cy-ay, e2z = cz-az;
            double e3x = dx-ax, e3y = dy-ay, e3z = dz-az;

            // Signed volume = det(e1,e2,e3) / 6
            double vol = (e1x*(e2y*e3z - e2z*e3y)
                        - e1y*(e2x*e3z - e2z*e3x)
                        + e1z*(e2x*e3y - e2y*e3x)) / 6.0;

            // All 6 edges
            double l01 = EdgeLen(ax,ay,az, bx,by,bz);
            double l02 = EdgeLen(ax,ay,az, cx,cy,cz);
            double l03 = EdgeLen(ax,ay,az, dx,dy,dz);
            double l12 = EdgeLen(bx,by,bz, cx,cy,cz);
            double l13 = EdgeLen(bx,by,bz, dx,dy,dz);
            double l23 = EdgeLen(cx,cy,cz, dx,dy,dz);

            double minEdge = Min6(l01,l02,l03,l12,l13,l23);
            double maxEdge = Max6(l01,l02,l03,l12,l13,l23);
            double edgeRatio = maxEdge > 0 ? minEdge / maxEdge : 0;

            // Mean ratio η = 12*(3V)^(2/3) / Σl²
            double sumL2 = l01*l01 + l02*l02 + l03*l03
                         + l12*l12 + l13*l13 + l23*l23;
            double absVol = Math.Abs(vol);
            double eta = 0;
            if (sumL2 > 0 && absVol > 0)
                eta = 12.0 * Math.Pow(3.0 * absVol, 2.0/3.0) / sumL2;

            // Dihedral angles (one per edge, 6 total)
            double minDih = double.MaxValue, maxDih = double.MinValue;

            void CheckEdge(double p0x, double p0y, double p0z,
                           double p1x, double p1y, double p1z,
                           double q0x, double q0y, double q0z,
                           double q1x, double q1y, double q1z)
            {
                // Edge (p0,p1), faces share q0 and q1 as the opposite vertices.
                // Face 1: (p0,p1,q0), outward away from q1
                // Face 2: (p0,p1,q1), outward away from q0
                double angle = DihedralAngle(
                    p0x,p0y,p0z, p1x,p1y,p1z,
                    q0x,q0y,q0z, q1x,q1y,q1z) * Rad2Deg;
                if (angle < minDih) minDih = angle;
                if (angle > maxDih) maxDih = angle;
            }

            CheckEdge(ax,ay,az, bx,by,bz, cx,cy,cz, dx,dy,dz); // edge 01
            CheckEdge(ax,ay,az, cx,cy,cz, bx,by,bz, dx,dy,dz); // edge 02
            CheckEdge(ax,ay,az, dx,dy,dz, bx,by,bz, cx,cy,cz); // edge 03
            CheckEdge(bx,by,bz, cx,cy,cz, ax,ay,az, dx,dy,dz); // edge 12
            CheckEdge(bx,by,bz, dx,dy,dz, ax,ay,az, cx,cy,cz); // edge 13
            CheckEdge(cx,cy,cz, dx,dy,dz, ax,ay,az, bx,by,bz); // edge 23

            return new TetQuality(eta, minDih, maxDih, vol, edgeRatio);
        }

        // ------------------------------------------------------------------
        // Dihedral angle along edge (p0,p1) between faces (p0,p1,q0) and (p0,p1,q1)
        // ------------------------------------------------------------------
        private static double DihedralAngle(
            double p0x, double p0y, double p0z,
            double p1x, double p1y, double p1z,
            double q0x, double q0y, double q0z,
            double q1x, double q1y, double q1z)
        {
            // Outward face normal of (p0,p1,q0), pointing away from q1
            double n1x, n1y, n1z;
            Cross(p1x-p0x,p1y-p0y,p1z-p0z,
                  q0x-p0x,q0y-p0y,q0z-p0z,
                  out n1x, out n1y, out n1z);
            if (Dot(n1x,n1y,n1z, q1x-p0x,q1y-p0y,q1z-p0z) > 0)
            { n1x=-n1x; n1y=-n1y; n1z=-n1z; }

            // Outward face normal of (p0,p1,q1), pointing away from q0
            double n2x, n2y, n2z;
            Cross(p1x-p0x,p1y-p0y,p1z-p0z,
                  q1x-p0x,q1y-p0y,q1z-p0z,
                  out n2x, out n2y, out n2z);
            if (Dot(n2x,n2y,n2z, q0x-p0x,q0y-p0y,q0z-p0z) > 0)
            { n2x=-n2x; n2y=-n2y; n2z=-n2z; }

            double len1 = Math.Sqrt(n1x*n1x + n1y*n1y + n1z*n1z);
            double len2 = Math.Sqrt(n2x*n2x + n2y*n2y + n2z*n2z);

            if (len1 < 1e-30 || len2 < 1e-30) return 0;

            double cosTheta = Dot(n1x,n1y,n1z, n2x,n2y,n2z) / (len1 * len2);
            cosTheta = Math.Max(-1.0, Math.Min(1.0, cosTheta));

            // Interior dihedral angle = π - angle_between_outward_normals
            return Math.PI - Math.Acos(cosTheta);
        }

        // ------------------------------------------------------------------
        // Arithmetic helpers (inline, no heap allocation)
        // ------------------------------------------------------------------
        private static double EdgeLen(double ax,double ay,double az,
                                       double bx,double by,double bz)
        {
            double dx=bx-ax, dy=by-ay, dz=bz-az;
            return Math.Sqrt(dx*dx+dy*dy+dz*dz);
        }

        private static void Cross(double ax,double ay,double az,
                                   double bx,double by,double bz,
                                   out double cx,out double cy,out double cz)
        {
            cx = ay*bz - az*by;
            cy = az*bx - ax*bz;
            cz = ax*by - ay*bx;
        }

        private static double Dot(double ax,double ay,double az,
                                   double bx,double by,double bz)
            => ax*bx + ay*by + az*bz;

        private static double Min6(double a,double b,double c,
                                    double d,double e,double f)
        {
            double m = a;
            if (b<m) m=b; if (c<m) m=c; if (d<m) m=d;
            if (e<m) m=e; if (f<m) m=f;
            return m;
        }

        private static double Max6(double a,double b,double c,
                                    double d,double e,double f)
        {
            double m = a;
            if (b>m) m=b; if (c>m) m=c; if (d>m) m=d;
            if (e>m) m=e; if (f>m) m=f;
            return m;
        }
    }
}

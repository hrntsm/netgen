using System;
using Rhino.Geometry;

namespace RhinoNetgenBridge
{
    /// <summary>
    /// Restricts the maximum mesh element size within a sphere of influence
    /// centred on a single point.
    ///
    /// <para>Use this to produce a finer mesh around a specific feature
    /// (e.g. a hole centre, a stress concentration, or an import point)
    /// without making the entire mesh fine.</para>
    ///
    /// <para>Example:</para>
    /// <code>
    ///   var restrictions = new[]
    ///   {
    ///       new PointSizeRestriction(new Point3d(10, 0, 5), maxElementSize: 0.5),
    ///       new PointSizeRestriction(new Point3d(-3, 2, 0), maxElementSize: 0.2),
    ///   };
    ///   TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp,
    ///       pointRestrictions: restrictions);
    /// </code>
    /// </summary>
    public sealed class PointSizeRestriction
    {
        /// <summary>Location of the restriction point.</summary>
        public Point3d Point { get; }

        /// <summary>
        /// Maximum allowed mesh element size at (and near) <see cref="Point"/>.
        /// Must be positive.
        /// </summary>
        public double MaxElementSize { get; }

        /// <param name="point">Restriction location in model space.</param>
        /// <param name="maxElementSize">Maximum element size at the point.</param>
        public PointSizeRestriction(Point3d point, double maxElementSize)
        {
            if (maxElementSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxElementSize),
                    "MaxElementSize must be positive.");
            Point          = point;
            MaxElementSize = maxElementSize;
        }

        /// <summary>
        /// Convenience constructor accepting individual coordinates.
        /// </summary>
        public PointSizeRestriction(double x, double y, double z, double maxElementSize)
            : this(new Point3d(x, y, z), maxElementSize) { }
    }


    /// <summary>
    /// Restricts the maximum mesh element size inside an axis-aligned bounding box.
    ///
    /// <para>All elements whose vertices lie inside the box will have size
    /// <see cref="MaxElementSize"/> or smaller.  Use this to refine a
    /// rectangular region of interest (e.g. around a critical cross-section
    /// or a sub-volume to be analysed in detail).</para>
    ///
    /// <para>Example:</para>
    /// <code>
    ///   var box = new BoundingBox(new Point3d(-5,-5,0), new Point3d(5,5,10));
    ///   var restriction = new BoxSizeRestriction(box, maxElementSize: 1.0);
    ///   TetrahedralMesh tet = NetgenMesher.GenerateFromBrep(brep, mp,
    ///       boxRestrictions: new[] { restriction });
    /// </code>
    /// </summary>
    public sealed class BoxSizeRestriction
    {
        /// <summary>Axis-aligned bounding box defining the restricted region.</summary>
        public BoundingBox Box { get; }

        /// <summary>
        /// Maximum allowed mesh element size inside <see cref="Box"/>.
        /// Must be positive.
        /// </summary>
        public double MaxElementSize { get; }

        /// <param name="box">Axis-aligned box in model space.</param>
        /// <param name="maxElementSize">Maximum element size inside the box.</param>
        public BoxSizeRestriction(BoundingBox box, double maxElementSize)
        {
            if (!box.IsValid)
                throw new ArgumentException("Box must be valid.", nameof(box));
            if (maxElementSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxElementSize),
                    "MaxElementSize must be positive.");
            Box            = box;
            MaxElementSize = maxElementSize;
        }

        /// <summary>
        /// Convenience constructor accepting two corner points.
        /// </summary>
        public BoxSizeRestriction(Point3d min, Point3d max, double maxElementSize)
            : this(new BoundingBox(min, max), maxElementSize) { }
    }
}

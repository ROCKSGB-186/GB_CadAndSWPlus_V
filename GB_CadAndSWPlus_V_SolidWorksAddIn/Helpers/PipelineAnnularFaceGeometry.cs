using System;
using System.Collections.Generic;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    internal sealed class PipelineCircularBoundary
    {
        public PipelineCircularBoundary(PipePoint3D center, PipeDirection3D axis, double radiusMm, double lengthMm)
        {
            Center = center;
            Axis = axis;
            RadiusMm = radiusMm;
            LengthMm = lengthMm;
        }

        public PipePoint3D Center { get; }
        public PipeDirection3D Axis { get; }
        public double RadiusMm { get; }
        public double LengthMm { get; }
    }

    internal sealed class PipelineAnnularFaceGeometry
    {
        private PipelineAnnularFaceGeometry(PipePoint3D center, PipeDirection3D normal, double innerRadius, double outerRadius)
        {
            Center = center;
            Normal = normal;
            InnerRadiusMm = innerRadius;
            OuterRadiusMm = outerRadius;
        }

        public PipePoint3D Center { get; }
        public PipeDirection3D Normal { get; }
        public double InnerRadiusMm { get; }
        public double OuterRadiusMm { get; }

        /// <summary>仅接受两个完整同心圆边构成的平面环形面；不能据此推断管口身份或 DN。</summary>
        public static PipelineAnnularFaceGeometry Create(PipeDirection3D normal, IReadOnlyList<PipelineCircularBoundary> boundaries)
        {
            if (boundaries == null) throw new ArgumentNullException(nameof(boundaries));
            if (boundaries.Count != 2 || boundaries[0] == null || boundaries[1] == null)
                throw new InvalidOperationException("当前试验仅支持由两个完整圆边构成的环形端面。");
            PipeDirection3D unitNormal = Unit(normal);
            foreach (PipelineCircularBoundary circle in boundaries)
            {
                if (!Finite(circle.RadiusMm) || !Finite(circle.LengthMm) || circle.RadiusMm <= 0 || circle.LengthMm <= 0)
                    throw new InvalidOperationException("圆边半径或长度无效。");
                double circumference = 2 * Math.PI * circle.RadiusMm;
                if (!Finite(circumference) || Math.Abs(circle.LengthMm - circumference) > Math.Max(1e-5, circumference * 1e-8))
                    throw new InvalidOperationException("端面边界不是完整圆。");
                PipeDirection3D axis = Unit(circle.Axis);
                double dot = axis.X * unitNormal.X + axis.Y * unitNormal.Y + axis.Z * unitNormal.Z;
                if (!Finite(dot) || Math.Abs(dot) < 1 - 1e-8)
                    throw new InvalidOperationException("圆边方向与端面法向不一致。");
            }
            PipelineCircularBoundary first = boundaries[0], second = boundaries[1];
            double outer = Math.Max(first.RadiusMm, second.RadiusMm);
            double inner = Math.Min(first.RadiusMm, second.RadiusMm);
            double tolerance = Math.Max(1e-5, outer * 1e-8);
            if (outer - inner <= tolerance)
                throw new InvalidOperationException("两个圆边半径过于接近，无法可靠识别环形端面。");
            if (Math.Abs(first.Center.X - second.Center.X) > tolerance ||
                Math.Abs(first.Center.Y - second.Center.Y) > tolerance ||
                Math.Abs(first.Center.Z - second.Center.Z) > tolerance)
                throw new InvalidOperationException("端面圆边不同心，不能作为环形参考点。");
            return new PipelineAnnularFaceGeometry(first.Center, unitNormal, inner, outer);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static PipeDirection3D Unit(PipeDirection3D vector)
        {
            double scale = Math.Max(Math.Abs(vector.X), Math.Max(Math.Abs(vector.Y), Math.Abs(vector.Z)));
            if (!Finite(scale) || scale == 0) throw new InvalidOperationException("端面方向无效。");
            double x = vector.X / scale, y = vector.Y / scale, z = vector.Z / scale;
            double length = Math.Sqrt(x * x + y * y + z * z);
            return new PipeDirection3D(x / length, y / length, z / length);
        }
    }
}

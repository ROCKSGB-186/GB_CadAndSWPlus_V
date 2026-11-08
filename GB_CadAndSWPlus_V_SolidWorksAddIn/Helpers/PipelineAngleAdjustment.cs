using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    internal sealed class PipelineAngleAdjustmentResult
    {
        public double OriginalAngleDegrees { get; internal set; }
        public double TargetAngleDegrees { get; internal set; }
        public PipeRoute? MoveEndRoute { get; internal set; }
        public PipeRoute? MoveCornerRoute { get; internal set; }
        public string? MoveEndUnavailableReason { get; internal set; }
        public string? MoveCornerUnavailableReason { get; internal set; }
    }

    /// <summary>仅计算建议，不修改路线或连接 SolidWorks；无效输入抛出异常。</summary>
    internal static class PipelineAngleAdjustment
    {
        private const double Epsilon = 2.2204460492503131e-16;
        private const double PlaneTolerance = 1e-8;
        private const double StandardAngleTolerance = 1e-12;

        internal static PipelineAngleAdjustmentResult Calculate(PipeRoute route, double targetAngleDegrees)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (targetAngleDegrees != 45 && targetAngleDegrees != 90 && targetAngleDegrees != 135)
                throw new ArgumentOutOfRangeException(nameof(targetAngleDegrees), "目标转向角只能是 45°、90° 或 135°。");
            if (route.StartKind != PipeEndpointKind.Free || route.EndKind != PipeEndpointKind.Free || route.Points.Count != 3)
                throw new ArgumentException("仅支持两个自由端的三点路线。", nameof(route));

            var start = V.From(route.Points[0]);
            var corner = V.From(route.Points[1]);
            var end = V.From(route.Points[2]);
            V incoming = corner - start, outgoing = end - corner;
            double inLength = incoming.Length(), outLength = outgoing.Length();
            if (inLength == 0 || outLength == 0)
                throw new ArgumentException("路线不能有零长度线段。", nameof(route));
            V u = incoming / inLength, v = outgoing / outLength;
            double sin = u.Cross(v).Length(), cos = Clamp(u.Dot(v));
            if (sin <= 1e-8)
                throw new ArgumentException("路线近共线或转向 180°，无法唯一确定路线平面。", nameof(route));

            double coordinateScale = Math.Max(start.MaxAbs(), Math.Max(corner.MaxAbs(), end.MaxAbs()));
            double scale = Math.Max(inLength, outLength);
            double tolerance = Math.Max(coordinateScale * (32 * Epsilon), scale * 1e-12);
            if (!Finite(tolerance) || tolerance <= 0 || tolerance >= Math.Min(inLength, outLength) * 1e-8)
                throw new ArgumentException("坐标相对于线段的精度不足，无法可靠计算建议。", nameof(route));

            var result = new PipelineAngleAdjustmentResult
            {
                OriginalAngleDegrees = Math.Atan2(sin, cos) * 180 / Math.PI,
                TargetAngleDegrees = targetAngleDegrees
            };
            double radians = targetAngleDegrees * Math.PI / 180;
            double targetCos = Math.Cos(radians), targetSin = Math.Sin(radians);
            V normal = u.Cross(v) / sin;

            // 原角度已经达到目标时保留原始坐标，避免不必要的舍入位移。
            if (Math.Abs(result.OriginalAngleDegrees - targetAngleDegrees) <= 1e-12)
            {
                try
                {
                    CheckCandidate(incoming, outgoing, normal, targetAngleDegrees, tolerance);
                    result.MoveEndRoute = new PipeRoute(new[] { route.Points[0], route.Points[1], route.Points[2] });
                    result.MoveCornerRoute = new PipeRoute(new[] { route.Points[0], route.Points[1], route.Points[2] });
                    return result;
                }
                catch (ArithmeticException)
                {
                    // 原点未通过严格复核时，分别尝试计算两个可表示的候选。
                }
            }

            try
            {
                // 同平面只有左右两个目标方向，原出口位于法向确定的正侧，故此方向最近。
                V adjustedEnd = corner + (u * targetCos + normal.Cross(u) * targetSin) * outLength;
                PipePoint3D p = adjustedEnd.Point();
                V actual = V.From(p) - corner;
                CheckCandidate(incoming, actual, normal, targetAngleDegrees, tolerance);
                if (Math.Abs(actual.Length() - outLength) > tolerance * 4)
                    throw new ArithmeticException("出口段长度精度不足。");
                result.MoveEndRoute = new PipeRoute(new[] { route.Points[0], route.Points[1], p });
            }
            catch (ArithmeticException ex) { result.MoveEndUnavailableReason = "无法可靠调整终点：" + ex.Message; }
            catch (ArgumentOutOfRangeException) { result.MoveEndUnavailableReason = "无法可靠调整终点：坐标超出有限数值范围。"; }

            try
            {
                // 固定两端时，同侧目标转角点构成一段开圆弧；端点不属于有效路线。
                V chord = end - start;
                double chordLength = chord.Length();
                if (chordLength <= tolerance * 4) throw new ArithmeticException("两固定端点过近。");
                V axis = chord / chordLength;
                V side = normal.Cross(axis);
                double signedSide = (corner - start).Dot(side);
                if (signedSide < 0) side = side * -1;
                if (Math.Abs(signedSide) <= tolerance * 4) throw new ArithmeticException("无法可靠判定原转角点所在侧。");
                V midpoint = start + chord * 0.5;
                double radius = chordLength / (2 * targetSin);
                double offset = chordLength * (targetCos / (2 * targetSin));
                V center = midpoint - side * offset;
                V radial = corner - center;
                double radialLength = radial.Length();
                // 投影方向对圆心附近的坐标误差高度敏感，不猜测最近点。
                if (radialLength <= tolerance * 1e8)
                    throw new ArithmeticException("原转角点过于接近圆心，无法可靠确定最近点。");
                V projected = center + (radial / radialLength) * radius;
                PipePoint3D p = projected.Point();
                V actualCorner = V.From(p);
                if ((actualCorner - start).Dot(side) <= tolerance * 4)
                    throw new ArithmeticException("无法确认最近点位于有效同侧开圆弧内；边界处可能只有无效端点的距离下确界。");
                CheckCandidate(actualCorner - start, end - actualCorner, normal, targetAngleDegrees, tolerance);
                if (Math.Abs((actualCorner - center).Length() - radius) > tolerance * 8)
                    throw new ArithmeticException("最近圆弧点的位置精度不足。");
                result.MoveCornerRoute = new PipeRoute(new[] { route.Points[0], p, route.Points[2] });
            }
            catch (ArithmeticException ex) { result.MoveCornerUnavailableReason = "无法可靠调整转角点：" + ex.Message; }
            catch (ArgumentOutOfRangeException) { result.MoveCornerUnavailableReason = "无法可靠调整转角点：坐标超出有限数值范围。"; }
            return result;
        }

        // 对实际写入的点重新以点积和路线平面复核，而不依赖构造公式。
        private static void CheckCandidate(V a, V b, V normal, double targetAngleDegrees, double tolerance)
        {
            double la = a.Length(), lb = b.Length();
            if (la <= tolerance * 4 || lb <= tolerance * 4)
                throw new ArithmeticException("候选路线包含零长或精度不足的线段。");
            // 与弯头核心使用同一归一化次序及标准角判定，避免边界舍入不一致。
            V ua = a.Unit(), ub = b.Unit();
            double dot = Clamp(ua.Dot(ub));
            double expected = targetAngleDegrees == 45 ? Math.PI / 4.0 : 3.0 * Math.PI / 4.0;
            bool angleInvalid = targetAngleDegrees == 90
                ? Math.Abs(dot) > StandardAngleTolerance
                : Math.Abs(Math.Acos(dot) - expected) > StandardAngleTolerance;
            if (angleInvalid)
                throw new ArithmeticException("候选路线的角度精度不足，无法通过弯头标准角复核。");
            if (Math.Abs(ua.Dot(normal)) > PlaneTolerance || Math.Abs(ub.Dot(normal)) > PlaneTolerance ||
                ua.Cross(ub).Dot(normal) <= 0)
                throw new ArithmeticException("候选路线的平面或转向复核失败。");
        }

        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        private static double Clamp(double x) { return Math.Max(-1, Math.Min(1, x)); }

        private struct V
        {
            private readonly double x, y, z;
            private V(double x, double y, double z)
            {
                if (!Finite(x) || !Finite(y) || !Finite(z)) throw new ArithmeticException("坐标或向量不是有限值。");
                this.x = x; this.y = y; this.z = z;
            }
            internal static V From(PipePoint3D p) { return new V(p.X, p.Y, p.Z); }
            internal PipePoint3D Point() { return new PipePoint3D(x, y, z); }
            internal double MaxAbs() { return Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))); }
            internal double Length()
            {
                double s = MaxAbs();
                if (s == 0) return 0;
                double a = x / s, b = y / s, c = z / s;
                double length = s * Math.Sqrt(a * a + b * b + c * c);
                if (!Finite(length) || length == 0) throw new ArithmeticException("向量长度溢出或下溢。");
                return length;
            }
            internal V Unit()
            {
                double scale = MaxAbs();
                if (scale == 0) throw new ArithmeticException("无法归一化零向量。");
                double a = x / scale, b = y / scale, c = z / scale;
                double norm = Math.Sqrt(a * a + b * b + c * c);
                return new V(a / norm, b / norm, c / norm);
            }
            internal double Dot(V b)
            {
                double d = x * b.x + y * b.y + z * b.z;
                if (!Finite(d)) throw new ArithmeticException("点积溢出。");
                return d;
            }
            internal V Cross(V b) { return new V(y * b.z - z * b.y, z * b.x - x * b.z, x * b.y - y * b.x); }
            public static V operator +(V a, V b) { return new V(a.x + b.x, a.y + b.y, a.z + b.z); }
            public static V operator -(V a, V b) { return new V(a.x - b.x, a.y - b.y, a.z - b.z); }
            public static V operator *(V a, double s) { return new V(a.x * s, a.y * s, a.z * s); }
            public static V operator /(V a, double s) { return new V(a.x / s, a.y / s, a.z / s); }
        }
    }
}

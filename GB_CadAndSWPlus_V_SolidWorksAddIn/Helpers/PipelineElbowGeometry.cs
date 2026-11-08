using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    /// <summary>三点标准转向弯管中心线试算结果；坐标和长度均以毫米计。</summary>
    internal sealed class PipelineElbowGeometryResult
    {
        internal PipelineElbowGeometryResult(double radiusMm, PipePoint3D start, PipePoint3D corner,
            PipePoint3D end, PipePoint3D entryTangent, PipePoint3D exitTangent, PipePoint3D center,
            double inletStraightLengthMm, double outletStraightLengthMm, double arcLengthMm, double totalLengthMm,
            double turnAngleRadians)
        {
            RadiusMm = radiusMm;
            Start = start;
            Corner = corner;
            End = end;
            EntryTangent = entryTangent;
            ExitTangent = exitTangent;
            Center = center;
            InletStraightLengthMm = inletStraightLengthMm;
            OutletStraightLengthMm = outletStraightLengthMm;
            ArcLengthMm = arcLengthMm;
            TotalLengthMm = totalLengthMm;
            TurnAngleRadians = turnAngleRadians;
            TurnAngleDegrees = turnAngleRadians * (180.0 / Math.PI);
        }

        public double RadiusMm { get; }
        public PipePoint3D Start { get; }
        public PipePoint3D Corner { get; }
        public PipePoint3D End { get; }
        public PipePoint3D EntryTangent { get; }
        public PipePoint3D ExitTangent { get; }
        public PipePoint3D Center { get; }
        public double InletStraightLengthMm { get; }
        public double OutletStraightLengthMm { get; }
        public double ArcLengthMm { get; }
        public double TotalLengthMm { get; }
        public double TurnAngleRadians { get; }
        public double TurnAngleDegrees { get; }
    }

    internal static class PipelineElbowGeometry
    {
        // 直角沿用原有方向余弦容差，其他标准角仅允许弧度浮点容差。
        private const double RightAngleTolerance = 1e-12;
        private const double AngleTolerance = 1e-12;
        private const double MinimumStraightMm = 1e-9;
        // 生成坐标相对于半径及剩余直管的最大允许分量误差。
        private const double GeometryRelativeTolerance = 1e-10;

        /// <summary>
        /// 仅计算三点、两自由端的 45、90、135 度中心线；不校验工程规格、半径适用性或干涉。
        /// 半径须为有限正数；标准角仅允许 1e-12 弧度偏差，直角沿用原方向余弦容差，
        /// 两端切线后的直管长度均须严格大于 1e-9 毫米。
        /// 输入无效抛出参数异常；数值溢出或表示精度不足抛出 ArithmeticException。
        /// </summary>
        internal static PipelineElbowGeometryResult Calculate(PipeRoute route, double radiusMm)
        {
            if (route == null) throw new ArgumentNullException(nameof(route), "路线不能为空。");
            if (route.Points.Count != 3 || route.StartKind != PipeEndpointKind.Free ||
                route.EndKind != PipeEndpointKind.Free)
                throw new ArgumentException("弯管路线必须恰好包含三个点，且两端均为自由端。", nameof(route));
            if (!IsFinite(radiusMm) || radiusMm <= 0)
                throw new ArgumentOutOfRangeException(nameof(radiusMm), "中心线半径必须为有限且大于零的毫米数值。");

            PipePoint3D start = route.Points[0], corner = route.Points[1], end = route.Points[2];
            Vector inlet = Difference(corner, start);
            Vector outlet = Difference(end, corner);
            double inletLength, outletLength;
            Vector u = Normalize(inlet, "入口", out inletLength);
            Vector v = Normalize(outlet, "出口", out outletLength);
            double dot = u.X * v.X + u.Y * v.Y + u.Z * v.Z;
            if (!IsFinite(dot))
                throw new ArithmeticException("路线方向计算溢出。");
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            double angle = Math.Acos(dot);
            if (Math.Abs(dot) > RightAngleTolerance &&
                Math.Abs(angle - Math.PI / 4.0) > AngleTolerance &&
                Math.Abs(angle - 3.0 * Math.PI / 4.0) > AngleTolerance)
                throw new ArgumentException("两段路线的前进方向转向角必须为 45、90 或 135 度；请先选择【查看角度调整方案】。", nameof(route));

            // 精确直角保留原有切距等于半径的行为，其他情况使用实际角度。
            double factor = dot == 0 ? 1.0 : Math.Tan(angle / 2.0);
            double setback = radiusMm * factor;
            if (!IsFinite(setback) || setback <= 0 ||
                Math.Abs(setback / radiusMm - factor) > GeometryRelativeTolerance * factor)
                throw new ArithmeticException("切距溢出或超出可表示精度。");
            double inletStraight = inletLength - setback;
            double outletStraight = outletLength - setback;
            if (inletStraight <= MinimumStraightMm)
                throw new ArgumentException("入口切线后的直管长度必须大于 1e-9 毫米。", nameof(route));
            if (outletStraight <= MinimumStraightMm)
                throw new ArgumentException("出口切线后的直管长度必须大于 1e-9 毫米。", nameof(route));
            if (inletStraight == inletLength || outletStraight == outletLength)
                throw new ArithmeticException("切距相对于线段长度的表示精度不足。");

            // 沿实际转向平面计算单位法向量，不将输入坐标或实际角度取整。
            double sine = Math.Sin(angle);
            Vector normal = new Vector((v.X - dot * u.X) / sine,
                (v.Y - dot * u.Y) / sine, (v.Z - dot * u.Z) / sine);
            Vector exitToCenter = new Vector(dot * normal.X - sine * u.X,
                dot * normal.Y - sine * u.Y, dot * normal.Z - sine * u.Z);
            PipePoint3D entry = Offset(corner, u, -setback);
            PipePoint3D exit = Offset(corner, v, setback);
            PipePoint3D center = Offset(entry, normal, radiusMm);

            // 相对半径和直管长度复核实际可表示的坐标，拒绝大偏移导致的坐标坍缩。
            CheckDisplacement(corner, entry, u, -setback);
            CheckDisplacement(corner, exit, v, setback);
            CheckDisplacement(entry, center, normal, radiusMm);
            CheckDisplacement(exit, center, exitToCenter, radiusMm);
            CheckDisplacement(start, entry, u, inletStraight);
            CheckDisplacement(exit, end, v, outletStraight);

            double arc = radiusMm * angle;
            double total = inletStraight + outletStraight + arc;
            if (!IsFinite(arc) || arc <= 0 ||
                Math.Abs(arc / radiusMm - angle) > GeometryRelativeTolerance ||
                !IsFinite(total) || total <= 0)
                throw new ArithmeticException("弯管中心线长度溢出或超出可表示精度。");

            return new PipelineElbowGeometryResult(radiusMm, start, corner, end, entry, exit, center,
                inletStraight, outletStraight, arc, total, angle);
        }

        private struct Vector
        {
            internal Vector(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal double X, Y, Z;
        }

        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static Vector Difference(PipePoint3D a, PipePoint3D b)
        {
            double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                throw new ArithmeticException("路线坐标差溢出，无法计算方向和长度。");
            return new Vector(x, y, z);
        }

        private static Vector Normalize(Vector vector, string segment, out double length)
        {
            double scale = Math.Max(Math.Abs(vector.X), Math.Max(Math.Abs(vector.Y), Math.Abs(vector.Z)));
            if (scale == 0) throw new ArgumentException(segment + "线段长度不能为零。");
            // 先按最大分量缩放，避免平方溢出或下溢。
            double x = vector.X / scale, y = vector.Y / scale, z = vector.Z / scale;
            double norm = Math.Sqrt(x * x + y * y + z * z);
            length = scale * norm;
            if (!IsFinite(length) || length <= 0)
                throw new ArithmeticException(segment + "线段长度溢出或超出可表示精度。");
            return new Vector(x / norm, y / norm, z / norm);
        }

        private static PipePoint3D Offset(PipePoint3D point, Vector direction, double distance)
        {
            double x = point.X + direction.X * distance;
            double y = point.Y + direction.Y * distance;
            double z = point.Z + direction.Z * distance;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                throw new ArithmeticException("切点或圆心坐标溢出。");
            return new PipePoint3D(x, y, z);
        }

        private static void CheckDisplacement(PipePoint3D from, PipePoint3D to, Vector direction, double distance)
        {
            Vector delta = Difference(to, from);
            // 除以实际距离而非乘容差，避免小半径下容差乘积下溢。
            if (!IsFinite(delta.X / distance) || !IsFinite(delta.Y / distance) || !IsFinite(delta.Z / distance) ||
                Math.Abs(delta.X / distance - direction.X) > GeometryRelativeTolerance ||
                Math.Abs(delta.Y / distance - direction.Y) > GeometryRelativeTolerance ||
                Math.Abs(delta.Z / distance - direction.Z) > GeometryRelativeTolerance)
                throw new ArithmeticException("坐标相对于半径或剩余直管长度的表示精度不足。");
        }
    }
}

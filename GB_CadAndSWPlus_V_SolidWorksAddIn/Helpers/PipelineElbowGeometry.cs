using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    /// <summary>三点直角弯管中心线试算结果；坐标和长度均以毫米计。</summary>
    internal sealed class PipelineElbowGeometryResult
    {
        internal PipelineElbowGeometryResult(double radiusMm, PipePoint3D start, PipePoint3D corner,
            PipePoint3D end, PipePoint3D entryTangent, PipePoint3D exitTangent, PipePoint3D center,
            double inletStraightLengthMm, double outletStraightLengthMm, double arcLengthMm, double totalLengthMm)
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
    }

    internal static class PipelineElbowGeometry
    {
        // 方向余弦只允许浮点舍入量级的偏差；每端剩余直管必须严格大于此绝对容差。
        private const double RightAngleTolerance = 1e-12;
        private const double MinimumStraightMm = 1e-9;
        // 生成坐标相对于半径及剩余直管的最大允许分量误差。
        private const double GeometryRelativeTolerance = 1e-10;

        /// <summary>
        /// 仅计算三点、两自由端的 90 度中心线；不校验工程规格、半径适用性或干涉。
        /// 半径须为有限正数；两方向点积绝对值不超过 1e-12，
        /// 两端切线后的直管长度均须严格大于 1e-9 毫米。
        /// 输入无效抛出参数异常；数值溢出或表示精度不足抛出 ArithmeticException。
        /// </summary>
        internal static PipelineElbowGeometryResult Calculate(PipeRoute route, double radiusMm)
        {
            if (route == null) throw new ArgumentNullException(nameof(route), "路线不能为空。");
            if (route.Points.Count != 3 || route.StartKind != PipeEndpointKind.Free ||
                route.EndKind != PipeEndpointKind.Free)
                throw new ArgumentException("直角弯管路线必须恰好包含三个点，且两端均为自由端。", nameof(route));
            if (!IsFinite(radiusMm) || radiusMm <= 0)
                throw new ArgumentOutOfRangeException(nameof(radiusMm), "中心线半径必须为有限且大于零的毫米数值。");

            PipePoint3D start = route.Points[0], corner = route.Points[1], end = route.Points[2];
            Vector inlet = Difference(corner, start);
            Vector outlet = Difference(end, corner);
            double inletLength, outletLength;
            Vector u = Normalize(inlet, "入口", out inletLength);
            Vector v = Normalize(outlet, "出口", out outletLength);
            double dot = u.X * v.X + u.Y * v.Y + u.Z * v.Z;
            if (!IsFinite(dot) || Math.Abs(dot) > RightAngleTolerance)
                throw new ArgumentException("两段路线必须在三维空间中成 90 度。", nameof(route));

            double inletStraight = inletLength - radiusMm;
            double outletStraight = outletLength - radiusMm;
            if (inletStraight <= MinimumStraightMm)
                throw new ArgumentException("入口切线后的直管长度必须大于 1e-9 毫米。", nameof(route));
            if (outletStraight <= MinimumStraightMm)
                throw new ArgumentException("出口切线后的直管长度必须大于 1e-9 毫米。", nameof(route));
            if (inletStraight == inletLength || outletStraight == outletLength)
                throw new ArithmeticException("半径相对于线段长度的表示精度不足。");

            // 切点距拐点均为一个半径，圆心由入口切点沿出口方向偏移一个半径。
            PipePoint3D entry = Offset(corner, u, -radiusMm);
            PipePoint3D exit = Offset(corner, v, radiusMm);
            PipePoint3D center = Offset(entry, v, radiusMm);

            // 相对半径和直管长度复核实际可表示的坐标，拒绝大偏移导致的坐标坍缩。
            CheckDisplacement(corner, entry, u, -radiusMm);
            CheckDisplacement(corner, exit, v, radiusMm);
            CheckDisplacement(entry, center, v, radiusMm);
            CheckDisplacement(exit, center, u, -radiusMm);
            CheckDisplacement(start, entry, u, inletStraight);
            CheckDisplacement(exit, end, v, outletStraight);

            double arc = radiusMm * (Math.PI / 2.0);
            double total = inletStraight + outletStraight + arc;
            if (!IsFinite(arc) || arc <= 0 ||
                Math.Abs(arc / radiusMm - Math.PI / 2.0) > GeometryRelativeTolerance ||
                !IsFinite(total) || total <= 0)
                throw new ArithmeticException("弯管中心线长度溢出或超出可表示精度。");

            return new PipelineElbowGeometryResult(radiusMm, start, corner, end, entry, exit, center,
                inletStraight, outletStraight, arc, total);
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

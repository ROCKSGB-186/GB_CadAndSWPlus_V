using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// 多段自由端路线的毫米中心线几何试算；不校验全局自交、干涉或工程规格。
    /// 路线段和转角点在错误消息中均按原路线从 1 开始编号。
    /// </summary>
    internal sealed class PipelineMultiSegmentPlan
    {
        private const double MinimumStraightMm = 1e-9;
        private const double RelativePrecisionTolerance = 1e-10;
        private static readonly BigInteger AbsoluteLimitSquaredDenominator = BigInteger.Pow(10, 12);
        private static readonly BigInteger RelativeLimitSquaredDenominator = BigInteger.Pow(10, 20);

        private PipelineMultiSegmentPlan(List<PipeRoute> straights, List<PipelineElbowGeometryResult> elbows,
            List<PipelineThreePiecePlan> elbowPlans,
            List<int> elbowPointNumbers, List<int> approximatePointNumbers, double maximumApproximationMm,
            double outsideDiameterMm, double wallThicknessMm, double insideDiameterMm)
        {
            Straights = new ReadOnlyCollection<PipeRoute>(new List<PipeRoute>(straights));
            Elbows = new ReadOnlyCollection<PipelineElbowGeometryResult>(new List<PipelineElbowGeometryResult>(elbows));
            ElbowPlans = new ReadOnlyCollection<PipelineThreePiecePlan>(new List<PipelineThreePiecePlan>(elbowPlans));
            ElbowPointNumbers = new ReadOnlyCollection<int>(new List<int>(elbowPointNumbers));
            ApproximatePointNumbers = new ReadOnlyCollection<int>(new List<int>(approximatePointNumbers));
            MaximumApproximationMm = maximumApproximationMm;
            OutsideDiameterMm = outsideDiameterMm;
            WallThicknessMm = wallThicknessMm;
            InsideDiameterMm = insideDiameterMm;
        }

        public IReadOnlyList<PipeRoute> Straights { get; }
        public IReadOnlyList<PipelineElbowGeometryResult> Elbows { get; }
        /// <summary>与弯头顺序一一对应的原始三件方案，供独立弯头预检和建模使用。</summary>
        internal IReadOnlyList<PipelineThreePiecePlan> ElbowPlans { get; }
        /// <summary>各弯头对应的原路线转角点号，从 1 开始。</summary>
        public IReadOnlyList<int> ElbowPointNumbers { get; }
        /// <summary>相对于预览直线有非零偏移的原始中间点号，从 1 开始。</summary>
        public IReadOnlyList<int> ApproximatePointNumbers { get; }
        /// <summary>中间点最大偏移的保守上界，单位为毫米；无近似时为零。</summary>
        public double MaximumApproximationMm { get; }
        public double OutsideDiameterMm { get; }
        public double WallThicknessMm { get; }
        public double InsideDiameterMm { get; }

        internal static PipelineMultiSegmentPlan Create(PipeRoute route, double radiusMm,
            double outsideDiameterMm, double wallThicknessMm)
        {
            return Create(route, radiusMm, outsideDiameterMm, wallThicknessMm, false);
        }

        /// <summary>
        /// 允许近似时只计算理论预览，不修改原路线或创建模型。
        /// 合并组所有原始中间点均须满足 ULP、绝对距离和最短原段相对距离的误差界。
        /// </summary>
        internal static PipelineMultiSegmentPlan Create(PipeRoute route, double radiusMm,
            double outsideDiameterMm, double wallThicknessMm, bool allowApproximateCollinear)
        {
            if (route == null) throw new ArgumentNullException(nameof(route), "路线不能为空。");
            if (route.Points.Count < 3 || route.StartKind != PipeEndpointKind.Free ||
                route.EndKind != PipeEndpointKind.Free)
                throw new ArgumentException("路线至少需要三个点，且两端均为自由端。", nameof(route));

            double insideDiameterMm = PipelineThreePiecePlan.ValidateDimensions(radiusMm, outsideDiameterMm, wallThicknessMm);
            int originalSegmentCount = route.Points.Count - 1;
            var originalDirections = new Direction[originalSegmentCount];
            var originalLengths = new double[originalSegmentCount];
            for (int i = 0; i < originalSegmentCount; i++)
                originalDirections[i] = GetDirection(route.Points[i], route.Points[i + 1], (i + 1).ToString(), out originalLengths[i]);

            // 严格共线自动合并；经允许时仅把满足局部保守误差界的点视为近似候选。
            var nodes = new List<int> { 0 };
            var exactPoints = new ExactPoint[route.Points.Count];
            for (int i = 0; i < exactPoints.Length; i++)
                exactPoints[i] = ToExactPoint(route.Points[i]);
            for (int i = 1; i < route.Points.Count - 1; i++)
            {
                int collinearDirection = ExactCollinearDirection(route.Points[i - 1], route.Points[i], route.Points[i + 1]);
                if (collinearDirection < 0)
                    throw new ArgumentException("原路线转角点 " + (i + 1) + "（相邻路线段 " + i +
                        "、" + (i + 1) + "）发生回转。", nameof(route));
                if (collinearDirection == 0 &&
                    (!allowApproximateCollinear || !CheckApproximateGroup(exactPoints, i - 1, i + 1, null, null)))
                    nodes.Add(i);
            }
            nodes.Add(route.Points.Count - 1);
            int segmentCount = nodes.Count - 1;
            var approximateGroups = new bool[segmentCount];
            var approximatePointNumbers = new List<int>();
            var maximumDeviation = new Deviation();
            if (allowApproximateCollinear)
            {
                // 整组合并后重新检查每个原始点，防止局部容差逐段累积。
                for (int i = 0; i < nodes.Count - 1; i++)
                {
                    if (nodes[i + 1] <= nodes[i] + 1) continue;
                    int previousCount = approximatePointNumbers.Count;
                    if (!CheckApproximateGroup(exactPoints, nodes[i], nodes[i + 1], approximatePointNumbers, maximumDeviation))
                        throw new ArithmeticException("原路线段 " + SegmentNumbers(nodes[i], nodes[i + 1]) +
                            " 的整组近似共线偏移、投影顺序或表示精度超出允许范围。");
                    approximateGroups[i] = approximatePointNumbers.Count > previousCount;
                }
            }
            var lengths = new double[segmentCount];
            var directions = new Direction[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                string segment = SegmentNumbers(nodes[i], nodes[i + 1]);
                directions[i] = GetDirection(route.Points[nodes[i]], route.Points[nodes[i + 1]], segment, out lengths[i]);
                // 严格共线时复核合并前后方向；近似组已精确核验偏距及单调投影，
                // 不额外施加方向差限值，但仍逐段复核各自坐标与方向的表示精度。
                for (int j = nodes[i]; j < nodes[i + 1]; j++)
                {
                    string originalSegment = (j + 1).ToString();
                    if (approximateGroups[i])
                        CheckStraight(route.Points[j], route.Points[j + 1], originalDirections[j], originalLengths[j], originalSegment);
                    else
                        CheckDirection(originalDirections[j], directions[i], originalSegment);
                }
            }

            // 沿用三件方案的尺寸、标准角、切点与圆心精度校验，不改变三点行为。
            var elbows = new List<PipelineElbowGeometryResult>(segmentCount - 1);
            var elbowPlans = new List<PipelineThreePiecePlan>(segmentCount - 1);
            var elbowPointNumbers = new List<int>(segmentCount - 1);
            for (int i = 1; i < nodes.Count - 1; i++)
            {
                var threePoints = new PipeRoute(new[] { route.Points[nodes[i - 1]], route.Points[nodes[i]], route.Points[nodes[i + 1]] });
                PipelineThreePiecePlan piece;
                try
                {
                    piece = PipelineThreePiecePlan.Create(threePoints, radiusMm, outsideDiameterMm, wallThicknessMm);
                }
                catch (ArgumentOutOfRangeException) { throw; }
                catch (ArgumentException ex)
                {
                    // 三件窗口有角度调整按钮，多段窗口没有；只移除不适用的操作提示。
                    string message = ex.Message.Replace("；请先选择【查看角度调整方案】。", "。");
                    if (allowApproximateCollinear && message.Contains("转向角必须为 45、90 或 135 度"))
                        message += " 该点未通过近似共线误差界或投影顺序校验。";
                    throw new ArgumentException("原路线转角点 " + (nodes[i] + 1) + "（相邻路线段 " +
                        SegmentNumbers(nodes[i - 1], nodes[i]) + "、" + SegmentNumbers(nodes[i], nodes[i + 1]) +
                        "）： " + message, nameof(route), ex);
                }
                catch (ArithmeticException ex)
                {
                    throw new ArithmeticException("原路线转角点 " + (nodes[i] + 1) + "（相邻路线段 " +
                        SegmentNumbers(nodes[i - 1], nodes[i]) + "、" + SegmentNumbers(nodes[i], nodes[i + 1]) +
                        "）： " + ex.Message, ex);
                }
                elbows.Add(piece.Elbow);
                elbowPlans.Add(piece);
                elbowPointNumbers.Add(nodes[i] + 1);
            }

            var straights = new List<PipeRoute>(segmentCount);
            for (int i = 0; i < segmentCount; i++)
            {
                PipelineElbowGeometryResult? before = i == 0 ? null : elbows[i - 1];
                PipelineElbowGeometryResult? after = i == elbows.Count ? null : elbows[i];
                string segment = SegmentNumbers(nodes[i], nodes[i + 1]);
                double remaining = lengths[i];
                if (before != null) remaining = SubtractSetback(remaining, Setback(before, directions[i - 1], directions[i]), segment);
                if (after != null) remaining = SubtractSetback(remaining, Setback(after, directions[i], directions[i + 1]), segment);
                if (remaining <= MinimumStraightMm)
                    throw new ArgumentException("原路线段 " + segment +
                        " 两侧切距相交或余下直管长度不大于 1e-9 毫米。", nameof(route));

                PipePoint3D start = before == null ? route.Points[nodes[i]] : before.ExitTangent;
                PipePoint3D end = after == null ? route.Points[nodes[i + 1]] : after.EntryTangent;
                CheckStraight(start, end, directions[i], remaining, segment);
                straights.Add(new PipeRoute(new[] { start, end }));
            }

            return new PipelineMultiSegmentPlan(straights, elbows, elbowPlans, elbowPointNumbers, approximatePointNumbers,
                MaximumDeviationMm(maximumDeviation), outsideDiameterMm, wallThicknessMm, insideDiameterMm);
        }

        private static string SegmentNumbers(int startIndex, int endIndex)
        {
            return endIndex == startIndex + 1 ? (startIndex + 1).ToString() :
                (startIndex + 1) + "-" + endIndex;
        }

        private static void CheckDirection(Direction original, Direction merged, string segment)
        {
            if (Math.Abs(original.X - merged.X) > RelativePrecisionTolerance ||
                Math.Abs(original.Y - merged.Y) > RelativePrecisionTolerance ||
                Math.Abs(original.Z - merged.Z) > RelativePrecisionTolerance)
                throw new ArithmeticException("原路线段 " + segment + " 合并后的方向表示精度不足。");
        }

        // 将每个有限 double 精确转换为以 2^-1074 为单位的整数，再做叉积和点积。
        private static BigInteger ExactCoordinate(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            long fraction = bits & 0x000fffffffffffffL;
            long mantissa = exponent == 0 ? fraction : fraction | 0x0010000000000000L;
            BigInteger result = new BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -result : result;
        }

        private struct ExactPoint
        {
            internal BigInteger X, Y, Z, Ulp;
        }

        private sealed class Deviation
        {
            internal BigInteger Numerator;
            internal BigInteger Denominator = BigInteger.One;
        }

        private static ExactPoint ToExactPoint(PipePoint3D point)
        {
            return new ExactPoint
            {
                X = ExactCoordinate(point.X), Y = ExactCoordinate(point.Y), Z = ExactCoordinate(point.Z),
                Ulp = BigInteger.Max(ExactUlp(point.X), BigInteger.Max(ExactUlp(point.Y), ExactUlp(point.Z)))
            };
        }

        // 单位为 2^-1074 mm；在二进制指数边界采用较大的相邻有限值间距。
        private static BigInteger ExactUlp(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            return BigInteger.One << (exponent == 0 ? 0 : exponent - 1);
        }

        private static BigInteger Dot(BigInteger ax, BigInteger ay, BigInteger az,
            BigInteger bx, BigInteger by, BigInteger bz)
        {
            return ax * bx + ay * by + az * bz;
        }

        // 用整数平方比较偏距，严格执行 min(sqrt(3)*U, 1e-6, 1e-10*最短段)。
        private static bool CheckApproximateGroup(ExactPoint[] points, int start, int end,
            List<int>? approximatePoints, Deviation? maximum)
        {
            ExactPoint first = points[start], last = points[end];
            BigInteger dx = last.X - first.X, dy = last.Y - first.Y, dz = last.Z - first.Z;
            BigInteger lengthSquared = Dot(dx, dy, dz, dx, dy, dz);
            if (lengthSquared.IsZero) return false;
            BigInteger ulp = first.Ulp, minimumSegmentSquared = BigInteger.Zero;
            for (int i = start + 1; i <= end; i++)
            {
                ulp = BigInteger.Max(ulp, points[i].Ulp);
                BigInteger sx = points[i].X - points[i - 1].X;
                BigInteger sy = points[i].Y - points[i - 1].Y;
                BigInteger sz = points[i].Z - points[i - 1].Z;
                BigInteger segmentSquared = Dot(sx, sy, sz, sx, sy, sz);
                if (segmentSquared.IsZero) return false;
                if (minimumSegmentSquared.IsZero || segmentSquared < minimumSegmentSquared)
                    minimumSegmentSquared = segmentSquared;
            }

            BigInteger ulpLimit = 3 * ulp * ulp * lengthSquared;
            BigInteger absoluteLimit = lengthSquared << 2148;
            BigInteger relativeLimit = lengthSquared * minimumSegmentSquared;
            BigInteger previousProjection = BigInteger.Zero;
            for (int i = start + 1; i < end; i++)
            {
                BigInteger px = points[i].X - first.X, py = points[i].Y - first.Y, pz = points[i].Z - first.Z;
                BigInteger projection = Dot(px, py, pz, dx, dy, dz);
                if (projection <= previousProjection || projection >= lengthSquared) return false;
                previousProjection = projection;
                BigInteger cx = py * dz - pz * dy, cy = pz * dx - px * dz, cz = px * dy - py * dx;
                BigInteger crossSquared = Dot(cx, cy, cz, cx, cy, cz);
                if (crossSquared > ulpLimit ||
                    crossSquared * AbsoluteLimitSquaredDenominator > absoluteLimit ||
                    crossSquared * RelativeLimitSquaredDenominator > relativeLimit)
                    return false;
                if (!crossSquared.IsZero && approximatePoints != null && maximum != null)
                {
                    approximatePoints.Add(i + 1);
                    if (crossSquared * maximum.Denominator > maximum.Numerator * lengthSquared)
                    {
                        maximum.Numerator = crossSquared;
                        maximum.Denominator = lengthSquared;
                    }
                }
            }
            return true;
        }

        // 二分找到不小于精确最大偏距的最小正 double，避免审计数值向下舍入。
        private static double MaximumDeviationMm(Deviation maximum)
        {
            if (maximum.Numerator.IsZero) return 0;
            long low = 0, high = BitConverter.DoubleToInt64Bits(1e-6) + 1;
            while (low + 1 < high)
            {
                long middle = low + (high - low) / 2;
                BigInteger value = ExactCoordinate(BitConverter.Int64BitsToDouble(middle));
                if (value * value * maximum.Denominator >= maximum.Numerator) high = middle;
                else low = middle;
            }
            return BitConverter.Int64BitsToDouble(high);
        }

        // 返回 1 表示严格同向共线，-1 表示严格反向共线，0 表示不共线。
        private static int ExactCollinearDirection(PipePoint3D a, PipePoint3D b, PipePoint3D c)
        {
            BigInteger ax = ExactCoordinate(a.X), ay = ExactCoordinate(a.Y), az = ExactCoordinate(a.Z);
            BigInteger bx = ExactCoordinate(b.X), by = ExactCoordinate(b.Y), bz = ExactCoordinate(b.Z);
            BigInteger cx = ExactCoordinate(c.X), cy = ExactCoordinate(c.Y), cz = ExactCoordinate(c.Z);
            BigInteger ux = bx - ax, uy = by - ay, uz = bz - az;
            BigInteger vx = cx - bx, vy = cy - by, vz = cz - bz;
            if (uy * vz != uz * vy || uz * vx != ux * vz || ux * vy != uy * vx) return 0;
            return (ux * vx + uy * vy + uz * vz).Sign;
        }

        private static double Setback(PipelineElbowGeometryResult elbow, Direction inlet, Direction outlet)
        {
            // 精确直角与 PipelineElbowGeometry 保持一致。
            double dot = inlet.X * outlet.X + inlet.Y * outlet.Y + inlet.Z * outlet.Z;
            return elbow.RadiusMm * (dot == 0 ? 1.0 : Math.Tan(elbow.TurnAngleRadians / 2.0));
        }

        private static double SubtractSetback(double length, double setback, string segment)
        {
            double result = length - setback;
            if (result <= MinimumStraightMm)
                throw new ArgumentException("原路线段 " + segment +
                    " 两侧切距相交或余下直管长度不大于 1e-9 毫米。");
            if (!IsFinite(result) || !IsFinite(setback) || setback <= 0 ||
                Math.Abs((length - result) / setback - 1.0) > RelativePrecisionTolerance)
                throw new ArithmeticException("原路线段 " + segment + " 的切距表示精度不足。");
            return result;
        }

        private struct Direction
        {
            internal double X, Y, Z;
        }

        private static Direction GetDirection(PipePoint3D start, PipePoint3D end, string segment, out double length)
        {
            double x = end.X - start.X, y = end.Y - start.Y, z = end.Z - start.Z;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
                throw new ArithmeticException("原路线段 " + segment + " 的坐标差溢出。");
            double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if (scale == 0)
                throw new ArgumentException("原路线段 " + segment + " 的长度不能为零。");
            x /= scale; y /= scale; z /= scale;
            double norm = Math.Sqrt(x * x + y * y + z * z);
            length = scale * norm;
            if (!IsFinite(length) || length <= 0)
                throw new ArithmeticException("原路线段 " + segment + " 的长度溢出或精度不足。");
            return new Direction { X = x / norm, Y = y / norm, Z = z / norm };
        }

        private static void CheckStraight(PipePoint3D start, PipePoint3D end, Direction direction,
            double length, string segment)
        {
            // 同时复核长度、方向和坐标表示；尤其防止共享段切点相交或舍入坍缩。
            double x = (end.X - start.X) / length;
            double y = (end.Y - start.Y) / length;
            double z = (end.Z - start.Z) / length;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) ||
                Math.Abs(x - direction.X) > RelativePrecisionTolerance ||
                Math.Abs(y - direction.Y) > RelativePrecisionTolerance ||
                Math.Abs(z - direction.Z) > RelativePrecisionTolerance)
                throw new ArithmeticException("原路线段 " + segment + " 的直管坐标、方向或长度表示精度不足。");
        }

        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}

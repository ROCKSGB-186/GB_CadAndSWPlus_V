using System;
using System.Collections.Generic;
using System.Numerics;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// 按原路线点、中心线半径和外径定义理想相切圆管，保守检查自身碰撞。
    /// 不检查实际 SolidWorks 实体、装配、外部障碍或工程公差。
    /// </summary>
    internal static class PipelineRouteSelfCollision
    {
        private const int MaximumPieces = 255;
        private const int MaximumPairs = 32385;
        private const int MaximumBoxPairs = 1000000;
        private const int SubdivisionDepth = 5;
        private const int StraightParts = 32;
        private const double MinimumMagnitude = 7.888609052210118e-31; // 2^-100
        private const double MaximumMagnitude = 1000000000.0;
        // 所有区间数值使用 2^-174 定点量化，长度以毫米计；整数运算向外取整。
        private static readonly BigInteger Unit = BigInteger.One << 174;

        private struct Interval
        {
            internal BigInteger Lo, Hi;
            internal Interval(BigInteger lo, BigInteger hi) { Lo = lo; Hi = hi; }
        }

        private struct Vector
        {
            internal Interval X, Y, Z;
            internal Vector(Interval x, Interval y, Interval z) { X = x; Y = y; Z = z; }
        }

        private sealed class Piece
        {
            internal string Name = string.Empty;
            internal Vector Start, End, Center;
            internal BigInteger Radius;
            internal bool IsElbow;
            internal Arc? IdealArc, ModeledArc;
            internal Box[]? Boxes;
        }

        private sealed class Arc
        {
            internal Vector Start, End, Center;
            internal Interval Radius;
            internal Box[]? Boxes;
        }

        private struct Box
        {
            internal Vector Min, Max;
        }

        private sealed class Bend
        {
            internal Piece Piece = null!;
            internal Interval Setback;
        }

        internal static void Validate(PipelineMultiSegmentPlan plan)
        {
            if (plan == null)
                throw new InvalidOperationException("路线方案为空，无法进行自碰撞预检。");
            if (plan.Straights == null || plan.Elbows == null || plan.ElbowPlans == null ||
                plan.ElbowPointNumbers == null || plan.Straights.Count == 0 ||
                plan.Elbows.Count != plan.Straights.Count - 1 ||
                plan.ElbowPlans.Count != plan.Elbows.Count || plan.ElbowPointNumbers.Count != plan.Elbows.Count)
                throw new InvalidOperationException("直管和弯头的数量或顺序不完整，无法进行自碰撞预检。");

            long count = (long)plan.Straights.Count + plan.Elbows.Count;
            if (count > MaximumPieces || count * (count - 1) / 2 > MaximumPairs)
                throw new InvalidOperationException("路线自碰撞预检计算预算不足，不能判定安全。");
            BigInteger diameter = Exact(plan.OutsideDiameterMm, "路线外径");
            if (diameter <= 0)
                throw new InvalidOperationException("路线外径无效，无法进行自碰撞预检。");
            BigInteger modeledDiameter = Exact(
                PipelineElbowBuildGeometry.Meters(plan.OutsideDiameterMm) / 2.0, "米制外半径") * 2000;
            BigInteger diameterBound = BigInteger.Max(diameter, modeledDiameter);
            if (modeledDiameter <= 0)
                throw new ArithmeticException("米制外半径精度不足。");

            var bends = new Bend[plan.Elbows.Count];
            var pieces = new Piece[(int)count];
            for (int i = 0; i < bends.Length; i++)
            {
                string name = "弯头 " + (i + 1) + "（原路线转角点 " + plan.ElbowPointNumbers[i] + "）";
                var ep = plan.ElbowPlans[i];
                if (ep == null || plan.Elbows[i] == null || !ReferenceEquals(ep.Elbow, plan.Elbows[i]) ||
                    ep.OutsideDiameterMm != plan.OutsideDiameterMm)
                    throw new InvalidOperationException(name + "的几何或外径与路线方案不一致。");
                if (i > 0 && (plan.Elbows[i - 1].Corner != ep.Elbow.Start ||
                    plan.Elbows[i - 1].End != ep.Elbow.Corner))
                    throw new InvalidOperationException(name + "与前一弯头的原路线点不一致。");
                try
                {
                    PipelineElbowBuildGeometry g = PipelineElbowBuildGeometry.Create(ep);
                    bends[i] = CreateBend(ep.Elbow, g, diameter, diameterBound, name);
                    pieces[2 * i + 1] = bends[i].Piece;
                }
                catch (Exception ex) when (ex is ArithmeticException || ex is ArgumentException || ex is InvalidOperationException)
                {
                    throw new ArithmeticException(name + "不能证明理想管形或数值精度：" + ex.Message, ex);
                }
            }

            for (int i = 0; i < plan.Straights.Count; i++)
            {
                string name = "直管 " + (i + 1);
                var route = plan.Straights[i];
                if (route == null || route.Points == null || route.Points.Count != 2)
                    throw new InvalidOperationException(name + "的端点不完整。");
                PipePoint3D originalStart = i == 0 ? route.Points[0] : plan.Elbows[i - 1].Corner;
                PipePoint3D originalEnd = i == bends.Length ? route.Points[1] : plan.Elbows[i].Corner;
                if ((i > 0 && route.Points[0] != plan.Elbows[i - 1].ExitTangent) ||
                    (i < bends.Length && route.Points[1] != plan.Elbows[i].EntryTangent) ||
                    (i == 0 && bends.Length > 0 && route.Points[0] != plan.Elbows[0].Start) ||
                    (i == bends.Length && bends.Length > 0 && route.Points[1] != plan.Elbows[i - 1].End))
                    throw new InvalidOperationException(name + "与相邻弯头或原路线的端点不一致。");
                try
                {
                    Vector start = Point(originalStart, name);
                    Vector end = Point(originalEnd, name);
                    Interval length = Length(Sub(end, start));
                    Interval remaining = length;
                    if (i > 0) remaining = Sub(remaining, bends[i - 1].Setback);
                    if (i < bends.Length) remaining = Sub(remaining, bends[i].Setback);
                    if (remaining.Lo <= 0)
                        throw new ArithmeticException("切距后的直管长度不能证明为正。");
                    Vector modeledStart = ModeledPoint(route.Points[0], name);
                    Vector modeledEnd = ModeledPoint(route.Points[1], name);
                    if (NormSquared(Sub(modeledEnd, modeledStart)).Lo <= 0)
                        throw new ArithmeticException("米制直管端点不能证明分离。");
                    pieces[2 * i] = new Piece
                    {
                        Name = name,
                        Start = Hull(i == 0 ? start : bends[i - 1].Piece.End, modeledStart),
                        End = Hull(i == bends.Length ? end : bends[i].Piece.Start, modeledEnd)
                    };
                }
                catch (ArithmeticException ex)
                {
                    throw new ArithmeticException(name + "不能证明理想管形或数值精度：" + ex.Message, ex);
                }
            }

            int boxPairs = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                for (int j = i + 1; j < pieces.Length; j++)
                {
                    Piece a = pieces[i], b = pieces[j];
                    if (j == i + 1 && a.IsElbow != b.IsElbow)
                    {
                        // 构造保证同一原路线、正长度、精确相切、转角小于半周且 R > 外半径。
                        // 仅理想圆弧截面与直管截面位于接头切平面两侧，仅公共接头可相交。
                        // 这不证明米制三点圆与米制直管相切，也不验证实际实体接头。
                        continue;
                    }
                    bool separated = Separated(a, b, diameterBound) ||
                        SeparatedBySubdivision(a, b, diameterBound, ref boxPairs);
                    if (!separated)
                        separated = SeparatedByProjection(a, b,
                            j == i + 2 && a.IsElbow && b.IsElbow ? pieces[i + 1] : null,
                            diameterBound, ref boxPairs);
                    if (!separated)
                        throw new InvalidOperationException(a.Name + "与" + b.Name +
                            "可能自交或碰撞，或保守包围无法证明分离。");
                }
            }
        }

        private static Bend CreateBend(PipelineElbowGeometryResult e, PipelineElbowBuildGeometry g,
            BigInteger diameter, BigInteger diameterBound, string name)
        {
            BigInteger radius = Exact(e.RadiusMm, name + "半径");
            if (radius <= 0 || radius * 2 <= diameter)
                throw new ArithmeticException("弯曲半径必须严格大于外半径。");

            Vector start = Point(e.Start, name), corner = Point(e.Corner, name), end = Point(e.End, name);
            Vector u = Divide(Sub(corner, start), Length(Sub(corner, start)));
            Vector v = Divide(Sub(end, corner), Length(Sub(end, corner)));
            Interval cosine = Dot(u, v);
            Interval one = Scalar(Unit);
            Interval numerator = Sub(one, cosine), denominator = Add(one, cosine);
            // cos(theta) 严格处于 (-1,1)，排除零角和半周；区间跨界时拒绝。
            if (numerator.Lo <= 0 || denominator.Lo <= 0)
                throw new ArithmeticException("无法证明转角严格介于 0° 和 180°。");
            Interval setback = Mul(Scalar(radius), Sqrt(Div(numerator, denominator)));
            Vector entry = Sub(corner, Mul(u, setback));
            Vector exit = Add(corner, Mul(v, setback));
            Vector normal = Sub(v, Mul(u, cosine));
            normal = Divide(normal, Length(normal));
            Vector center = Add(entry, Mul(normal, Scalar(radius)));
            Vector middleDirection = Add(Sub(entry, center), Sub(exit, center));
            Vector middle = Add(center, Mul(Divide(middleDirection, Length(middleDirection)), Scalar(radius)));

            // 独立以区间上界复核既有米制建模输入与本理想模型的差异。
            // 每个分量最多 1e-6 毫米，且不超过半径和外径各自的百万分之一。
            // 这不是对实际实体或工程公差的保证。
            BigInteger limit = BigInteger.Min(Unit / 1000000,
                BigInteger.Min(radius / 1000000, diameter / 1000000));
            if (limit <= 0) throw new ArithmeticException("尺寸过小，无法建立偏差上界。");
            CheckNear(entry, g.Start, limit, "入口");
            CheckNear(exit, g.End, limit, "出口");
            CheckNear(center, g.Center, limit, "圆心");
            CheckNear(middle, g.Mid, limit, "中点");
            CheckNear(Scalar(radius), Exact(g.Radius, "米制弯曲半径") * 1000, limit, "半径");
            CheckNear(Scalar(diameter), Exact(g.Outer, "米制外半径") * 2000, limit, "外径");

            // 对米制三点确定的数学圆另行求区间圆心和半径，包围整个圆。
            // 这使非相邻检查同时覆盖理想圆弧和舍入后的三点圆弧，不假定 PositionTolerance 是误差界。
            Vector gs = MetersPoint(g.Start, name), gm = MetersPoint(g.Mid, name), ge = MetersPoint(g.End, name);
            Vector gu = Sub(gm, gs), gv = Sub(ge, gs), gw = Cross(gu, gv);
            Interval gu2 = NormSquared(gu), gv2 = NormSquared(gv), gw2 = NormSquared(gw);
            Vector actualCenter = Add(gs, Divide(Add(Mul(Cross(gv, gw), gu2),
                Mul(Cross(gw, gu), gv2)), Add(gw2, gw2)));
            Interval actualRadius = Length(Sub(gs, actualCenter));
            if (actualRadius.Lo * 2 <= diameterBound)
                throw new ArithmeticException("三点圆的半径不能证明大于外半径。");
            Vector ra = Sub(gs, actualCenter), rb = Sub(ge, actualCenter), rm = Sub(gm, actualCenter);
            Vector arcNormal = Cross(ra, rb);
            if (Dot(Cross(ra, rm), arcNormal).Lo <= 0 ||
                Dot(Cross(rm, rb), arcNormal).Lo <= 0)
                throw new ArithmeticException("无法证明米制三点确定的是小于半周的短弧。");
            return new Bend
            {
                Setback = setback,
                Piece = new Piece
                {
                    Name = name, IsElbow = true, Start = Hull(entry, gs), End = Hull(exit, ge),
                    Center = Hull(center, actualCenter), Radius = BigInteger.Max(radius, actualRadius.Hi),
                    IdealArc = new Arc { Start = entry, End = exit, Center = center, Radius = Scalar(radius) },
                    ModeledArc = new Arc { Start = gs, End = ge, Center = actualCenter, Radius = actualRadius }
                }
            };
        }

        private static void CheckNear(Vector ideal, PipelineElbowBuildGeometry.Vector actual, BigInteger limit, string name)
        {
            CheckNear(ideal.X, Exact(actual.X, name) * 1000, limit, name + " X");
            CheckNear(ideal.Y, Exact(actual.Y, name) * 1000, limit, name + " Y");
            CheckNear(ideal.Z, Exact(actual.Z, name) * 1000, limit, name + " Z");
        }

        private static void CheckNear(Interval ideal, BigInteger actual, BigInteger limit, string name)
        {
            if (BigInteger.Max(BigInteger.Abs(ideal.Lo - actual), BigInteger.Abs(ideal.Hi - actual)) > limit)
                throw new ArithmeticException(name + "与理想几何的保守偏差上界超限。");
        }

        private static bool Separated(Piece a, Piece b, BigInteger diameter)
        {
            Vector amin = a.IsElbow ? a.Center : BoundsMin(a.Start, a.End);
            Vector amax = a.IsElbow ? a.Center : BoundsMax(a.Start, a.End);
            Vector bmin = b.IsElbow ? b.Center : BoundsMin(b.Start, b.End);
            Vector bmax = b.IsElbow ? b.Center : BoundsMax(b.Start, b.End);
            BigInteger dx = Gap(amin.X.Lo, amax.X.Hi, bmin.X.Lo, bmax.X.Hi);
            BigInteger dy = Gap(amin.Y.Lo, amax.Y.Hi, bmin.Y.Lo, bmax.Y.Hi);
            BigInteger dz = Gap(amin.Z.Lo, amax.Z.Hi, bmin.Z.Lo, bmax.Z.Hi);
            BigInteger limit = a.Radius + b.Radius + diameter;
            return dx * dx + dy * dy + dz * dz > limit * limit;
        }

        private static bool SeparatedBySubdivision(Piece a, Piece b, BigInteger diameter, ref int used)
        {
            try
            {
                Box[] boxesA = a.Boxes ?? (a.Boxes = MakeBoxes(a));
                Box[] boxesB = b.Boxes ?? (b.Boxes = MakeBoxes(b));
                BigInteger limit = diameter * diameter;
                foreach (Box x in boxesA)
                {
                    foreach (Box y in boxesB)
                    {
                        if (++used > MaximumBoxPairs)
                            throw new InvalidOperationException(a.Name + "与" + b.Name +
                                "的细分检查超出计算预算，不能判定安全。");
                        BigInteger dx = Gap(x.Min.X.Lo, x.Max.X.Hi, y.Min.X.Lo, y.Max.X.Hi);
                        BigInteger dy = Gap(x.Min.Y.Lo, x.Max.Y.Hi, y.Min.Y.Lo, y.Max.Y.Hi);
                        BigInteger dz = Gap(x.Min.Z.Lo, x.Max.Z.Hi, y.Min.Z.Lo, y.Max.Z.Hi);
                        if (dx * dx + dy * dy + dz * dz <= limit) return false;
                    }
                }
                return true;
            }
            catch (ArithmeticException ex)
            {
                throw new ArithmeticException(a.Name + "与" + b.Name +
                    "的细分包围无法可靠计算：" + ex.Message, ex);
            }
        }

        private static Box[] MakeBoxes(Piece piece)
        {
            var boxes = new List<Box>();
            if (piece.IsElbow)
            {
                boxes.AddRange(ArcBoxes(piece.IdealArc!));
                boxes.AddRange(ArcBoxes(piece.ModeledArc!));
            }
            else
            {
                Vector previous = piece.Start;
                for (int i = 1; i <= StraightParts; i++)
                {
                    // 两端点区间的凸组合包围理想直管和米制输入直管的对应部分。
                    Vector next = Divide(Add(Mul(piece.Start, Scalar(Unit * (StraightParts - i))),
                        Mul(piece.End, Scalar(Unit * i))), Scalar(Unit * StraightParts));
                    boxes.Add(Enclose(previous, next, BigInteger.Zero));
                    previous = next;
                }
            }
            return boxes.ToArray();
        }

        private static Box[] ArcBoxes(Arc arc)
        {
            if (arc.Boxes == null)
            {
                var boxes = new List<Box>();
                AddArcBoxes(arc, arc.Start, arc.End, SubdivisionDepth, boxes);
                arc.Boxes = boxes.ToArray();
            }
            return arc.Boxes;
        }

        private static void AddArcBoxes(Arc arc, Vector start, Vector end, int depth, List<Box> boxes)
        {
            if (depth == 0)
            {
                // 小于半周的圆弧到弦的最大距离为弓高；
                // 弓高 = c²/[4(R+sqrt(R²-c²/4))] <= c²/(4R)。
                Interval chordSquared = NormSquared(Sub(end, start));
                Interval sagitta = Div(chordSquared, Mul(Scalar(4 * Unit), arc.Radius));
                boxes.Add(Enclose(start, end, sagitta.Hi));
                return;
            }
            Vector sum = Add(Sub(start, arc.Center), Sub(end, arc.Center));
            Vector mid = Add(arc.Center, Mul(Divide(sum, Length(sum)), arc.Radius));
            AddArcBoxes(arc, start, mid, depth - 1, boxes);
            AddArcBoxes(arc, mid, end, depth - 1, boxes);
        }

        private static Box Enclose(Vector start, Vector end, BigInteger padding)
        {
            Vector min = BoundsMin(start, end), max = BoundsMax(start, end);
            Interval p = Scalar(padding);
            Vector pad = new Vector(p, p, p);
            return new Box { Min = Sub(min, pad), Max = Add(max, pad) };
        }

        private static bool SeparatedByProjection(Piece a, Piece b, Piece? middleStraight,
            BigInteger diameter, ref int used)
        {
            try
            {
                BigInteger outer = Ceiling(diameter, 2);
                Interval zero = Scalar(BigInteger.Zero), one = Scalar(Unit);
                // 坐标轴、直管方向以及两弯头之间的直管方向均为候选分离轴。
                // 找不到分离轴不代表碰撞，必须按风险拒绝。
                if (SeparatedOnAxis(a, b, new Vector(one, zero, zero), outer, ref used) ||
                    SeparatedOnAxis(a, b, new Vector(zero, one, zero), outer, ref used) ||
                    SeparatedOnAxis(a, b, new Vector(zero, zero, one), outer, ref used))
                    return true;
                if (!a.IsElbow && SeparatedOnAxis(a, b, StraightDirection(a), outer, ref used))
                    return true;
                if (!b.IsElbow && SeparatedOnAxis(a, b, StraightDirection(b), outer, ref used))
                    return true;
                if (!a.IsElbow || !b.IsElbow)
                {
                    // 候选横向轴可证明斜直管与另一管件分离，且不会跳过保守投影复核。
                    Piece straight = a.IsElbow ? b : a;
                    Piece other = a.IsElbow ? a : b;
                    Vector direction = StraightDirection(straight);
                    Vector offset = Sub(other.Start, straight.Start);
                    Vector lateral = Cross(direction, Cross(offset, direction));
                    if (NormSquared(lateral).Lo > 0 &&
                        SeparatedOnAxis(a, b, Divide(lateral, Length(lateral)), outer, ref used))
                        return true;
                }
                return middleStraight != null &&
                    SeparatedOnAxis(a, b, StraightDirection(middleStraight), outer, ref used);
            }
            catch (ArithmeticException ex)
            {
                throw new ArithmeticException(a.Name + "与" + b.Name +
                    "的截面投影无法可靠计算：" + ex.Message, ex);
            }
        }

        private static Vector StraightDirection(Piece straight)
        {
            // 端点区间同时包含理想直管和米制输入直管；归一化向外取整。
            Vector direction = Sub(straight.End, straight.Start);
            return Divide(direction, Length(direction));
        }

        private static bool SeparatedOnAxis(Piece a, Piece b, Vector axis, BigInteger outer, ref int used)
        {
            Interval first = ProjectPiece(a, axis, outer, a.Name, b.Name, ref used);
            Interval second = ProjectPiece(b, axis, outer, a.Name, b.Name, ref used);
            return first.Hi < second.Lo || second.Hi < first.Lo;
        }

        private static Interval ProjectPiece(Piece piece, Vector axis, BigInteger outer,
            string firstName, string secondName, ref int used)
        {
            if (piece.IsElbow)
                return Hull(ProjectArc(piece.IdealArc!, axis, outer, firstName, secondName, ref used),
                    ProjectArc(piece.ModeledArc!, axis, outer, firstName, secondName, ref used));
            if (++used > MaximumBoxPairs)
                throw new InvalidOperationException(firstName + "与" + secondName +
                    "的截面投影超出计算预算，不能判定安全。");
            Interval centerline = Hull(Dot(axis, piece.Start), Dot(axis, piece.End));
            Vector tangent = StraightDirection(piece);
            BigInteger factor = BigInteger.Min(Unit, SqrtNonnegative(NormSquared(Cross(axis, tangent))).Hi);
            BigInteger pad = Ceiling(outer * factor, Unit);
            return new Interval(centerline.Lo - pad, centerline.Hi + pad);
        }

        private static Interval ProjectArc(Arc arc, Vector axis, BigInteger outer,
            string firstName, string secondName, ref int used)
        {
            Vector ra = Sub(arc.Start, arc.Center), rb = Sub(arc.End, arc.Center);
            Vector normal = Cross(ra, rb);
            normal = Divide(normal, Length(normal));
            Interval result = default(Interval);
            bool first = true;
            foreach (Box box in ArcBoxes(arc))
            {
                if (++used > MaximumBoxPairs)
                    throw new InvalidOperationException(firstName + "与" + secondName +
                        "的截面投影超出计算预算，不能判定安全。");
                Vector points = Hull(box.Min, box.Max);
                Vector radial = Divide(Sub(points, arc.Center), arc.Radius);
                Vector tangent = Cross(normal, radial);
                // 每个圆形截面位于切线法平面，沿单位轴的投影半宽为 r*|轴×切线|。
                // 区间包围对应整段圆弧；真实单位向量叉积长度不超过 1。
                Interval crossLength = SqrtNonnegative(NormSquared(Cross(axis, tangent)));
                BigInteger factor = BigInteger.Min(Unit, crossLength.Hi);
                BigInteger pad = Ceiling(outer * factor, Unit);
                Interval projection = Dot(axis, points);
                projection = new Interval(projection.Lo - pad, projection.Hi + pad);
                result = first ? projection : Hull(result, projection);
                first = false;
            }
            return result;
        }

        private static BigInteger Gap(BigInteger amin, BigInteger amax, BigInteger bmin, BigInteger bmax) =>
            BigInteger.Max(BigInteger.Zero, BigInteger.Max(bmin - amax, amin - bmax));

        private static Vector BoundsMin(Vector a, Vector b) => new Vector(
            Scalar(BigInteger.Min(a.X.Lo, b.X.Lo)), Scalar(BigInteger.Min(a.Y.Lo, b.Y.Lo)),
            Scalar(BigInteger.Min(a.Z.Lo, b.Z.Lo)));
        private static Vector BoundsMax(Vector a, Vector b) => new Vector(
            Scalar(BigInteger.Max(a.X.Hi, b.X.Hi)), Scalar(BigInteger.Max(a.Y.Hi, b.Y.Hi)),
            Scalar(BigInteger.Max(a.Z.Hi, b.Z.Hi)));
        private static Vector Hull(Vector a, Vector b) => new Vector(
            new Interval(BigInteger.Min(a.X.Lo, b.X.Lo), BigInteger.Max(a.X.Hi, b.X.Hi)),
            new Interval(BigInteger.Min(a.Y.Lo, b.Y.Lo), BigInteger.Max(a.Y.Hi, b.Y.Hi)),
            new Interval(BigInteger.Min(a.Z.Lo, b.Z.Lo), BigInteger.Max(a.Z.Hi, b.Z.Hi)));
        private static Interval Hull(Interval a, Interval b) =>
            new Interval(BigInteger.Min(a.Lo, b.Lo), BigInteger.Max(a.Hi, b.Hi));

        private static Interval Scalar(BigInteger n) => new Interval(n, n);
        private static Interval Add(Interval a, Interval b) => new Interval(a.Lo + b.Lo, a.Hi + b.Hi);
        private static Interval Sub(Interval a, Interval b) => new Interval(a.Lo - b.Hi, a.Hi - b.Lo);
        private static Vector Add(Vector a, Vector b) => new Vector(Add(a.X, b.X), Add(a.Y, b.Y), Add(a.Z, b.Z));
        private static Vector Sub(Vector a, Vector b) => new Vector(Sub(a.X, b.X), Sub(a.Y, b.Y), Sub(a.Z, b.Z));
        private static Vector Mul(Vector a, Interval b) => new Vector(Mul(a.X, b), Mul(a.Y, b), Mul(a.Z, b));
        private static Vector Divide(Vector a, Interval b) => new Vector(Div(a.X, b), Div(a.Y, b), Div(a.Z, b));
        private static Interval Dot(Vector a, Vector b) =>
            Add(Add(Mul(a.X, b.X), Mul(a.Y, b.Y)), Mul(a.Z, b.Z));
        private static Vector Cross(Vector a, Vector b) => new Vector(
            Sub(Mul(a.Y, b.Z), Mul(a.Z, b.Y)), Sub(Mul(a.Z, b.X), Mul(a.X, b.Z)),
            Sub(Mul(a.X, b.Y), Mul(a.Y, b.X)));
        private static Interval NormSquared(Vector a) => Add(Add(Square(a.X), Square(a.Y)), Square(a.Z));
        private static Interval Length(Vector a) => Sqrt(NormSquared(a));

        private static Interval Mul(Interval a, Interval b)
        {
            BigInteger x = a.Lo * b.Lo, y = a.Lo * b.Hi, z = a.Hi * b.Lo, w = a.Hi * b.Hi;
            return new Interval(Floor(BigInteger.Min(BigInteger.Min(x, y), BigInteger.Min(z, w)), Unit),
                Ceiling(BigInteger.Max(BigInteger.Max(x, y), BigInteger.Max(z, w)), Unit));
        }

        private static Interval Square(Interval a)
        {
            BigInteger x = a.Lo * a.Lo, y = a.Hi * a.Hi;
            BigInteger lo = a.Lo <= 0 && a.Hi >= 0 ? BigInteger.Zero : BigInteger.Min(x, y);
            return new Interval(Floor(lo, Unit), Ceiling(BigInteger.Max(x, y), Unit));
        }

        private static Interval Div(Interval a, Interval b)
        {
            if (b.Lo <= 0) throw new ArithmeticException("区间除数不能证明为正。");
            BigInteger x = a.Lo * Unit, y = a.Hi * Unit;
            BigInteger lo = BigInteger.Min(Floor(x, b.Lo), Floor(x, b.Hi));
            BigInteger hi = BigInteger.Max(Ceiling(y, b.Lo), Ceiling(y, b.Hi));
            return new Interval(lo, hi);
        }

        private static BigInteger Floor(BigInteger a, BigInteger positive)
        {
            BigInteger remainder;
            BigInteger q = BigInteger.DivRem(a, positive, out remainder);
            return remainder.Sign < 0 ? q - 1 : q;
        }

        private static BigInteger Ceiling(BigInteger a, BigInteger positive) => -Floor(-a, positive);

        private static Interval Sqrt(Interval a)
        {
            if (a.Lo <= 0) throw new ArithmeticException("平方根区间不能证明严格为正。");
            BigInteger lo = IntegerSqrt(a.Lo * Unit), hiValue = a.Hi * Unit;
            BigInteger hi = IntegerSqrt(hiValue);
            if (hi * hi != hiValue) hi++;
            return new Interval(lo, hi);
        }

        private static Interval SqrtNonnegative(Interval a)
        {
            if (a.Lo < 0) throw new ArithmeticException("平方和区间下界无效。");
            BigInteger lo = IntegerSqrt(a.Lo * Unit), hiValue = a.Hi * Unit;
            BigInteger hi = IntegerSqrt(hiValue);
            if (hi * hi != hiValue) hi++;
            return new Interval(lo, hi);
        }

        private static BigInteger IntegerSqrt(BigInteger n)
        {
            if (n <= 0) return BigInteger.Zero;
            byte[] bytes = n.ToByteArray();
            int bits = bytes.Length * 8;
            BigInteger x = BigInteger.One << ((bits + 1) / 2);
            while (true)
            {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        private static Vector Point(PipePoint3D p, string name) =>
            new Vector(Scalar(Exact(p.X, name)), Scalar(Exact(p.Y, name)), Scalar(Exact(p.Z, name)));

        private static Vector MetersPoint(PipelineElbowBuildGeometry.Vector p, string name) =>
            new Vector(Scalar(Exact(p.X, name) * 1000), Scalar(Exact(p.Y, name) * 1000),
                Scalar(Exact(p.Z, name) * 1000));

        private static Vector ModeledPoint(PipePoint3D p, string name) =>
            MetersPoint(new PipelineElbowBuildGeometry.Vector(PipelineElbowBuildGeometry.Meters(p.X),
                PipelineElbowBuildGeometry.Meters(p.Y), PipelineElbowBuildGeometry.Meters(p.Z)), name);

        private static BigInteger Exact(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > MaximumMagnitude ||
                (value != 0 && Math.Abs(value) < MinimumMagnitude))
                throw new ArithmeticException(name + "的数值超出精确预检支持范围。");
            if (value == 0) return BigInteger.Zero;
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            long mantissa = (bits & 0x000fffffffffffffL) | 0x0010000000000000L;
            BigInteger integer = new BigInteger(mantissa) << (exponent - 901);
            return bits < 0 ? -integer : integer;
        }
    }
}

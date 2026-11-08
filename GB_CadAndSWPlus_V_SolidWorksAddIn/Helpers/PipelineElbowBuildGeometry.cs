using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    // 纯数学预检：所有坐标、半径、长度及体积分别使用米和立方米。
    internal sealed class PipelineElbowBuildGeometry
    {
        internal struct Vector
        {
            internal double X, Y, Z;
            internal Vector(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Vector operator +(Vector a, Vector b) => new Vector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector operator -(Vector a, Vector b) => new Vector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector operator *(Vector a, double s) => new Vector(a.X * s, a.Y * s, a.Z * s);
            internal double Dot(Vector b) => X * b.X + Y * b.Y + Z * b.Z;
            internal Vector Cross(Vector b) => new Vector(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
            internal double Length()
            {
                PipePoint3D.RequireFinite(X, "向量"); PipePoint3D.RequireFinite(Y, "向量"); PipePoint3D.RequireFinite(Z, "向量");
                double s = Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
                if (s == 0) return 0;
                double x = X / s, y = Y / s, z = Z / s;
                double length = s * Math.Sqrt(x * x + y * y + z * z);
                PipePoint3D.RequireFinite(length, "向量长度");
                return length;
            }
            internal Vector Unit()
            {
                double length = Length();
                if (length <= 0) throw new ArithmeticException("无法归一化零向量。");
                return new Vector(X / length, Y / length, Z / length);
            }
        }

        internal Vector Start, End, Center, Mid, Axis, StartTangent, EndTangent;
        internal double Radius, Inner, Outer, Wall, ArcLength, Volume, PositionTolerance, RadiusTolerance;
        public double TurnAngleRadians { get; private set; }
        public double TurnAngleDegrees { get; private set; }

        internal static PipelineElbowBuildGeometry Create(PipelineThreePiecePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var e = plan.Elbow ?? throw new ArgumentException("弯头几何不可用。");
            // 重算方案，确保入口、出口均为自由端，且尺寸和几何仍是已验证方案。
            if (plan.InletRoute.StartKind != PipeEndpointKind.Free || plan.InletRoute.EndKind != PipeEndpointKind.Free ||
                plan.OutletRoute.StartKind != PipeEndpointKind.Free || plan.OutletRoute.EndKind != PipeEndpointKind.Free)
                throw new ArgumentException("仅支持自由端方案。");
            var checkedPlan = PipelineThreePiecePlan.Create(new PipeRoute(new[] { e.Start, e.Corner, e.End }),
                e.RadiusMm, plan.OutsideDiameterMm, plan.WallThicknessMm);
            if (checkedPlan.Elbow.EntryTangent != e.EntryTangent || checkedPlan.Elbow.ExitTangent != e.ExitTangent ||
                checkedPlan.Elbow.Center != e.Center || checkedPlan.InsideDiameterMm != plan.InsideDiameterMm ||
                checkedPlan.Elbow.ArcLengthMm != e.ArcLengthMm ||
                checkedPlan.Elbow.TurnAngleRadians != e.TurnAngleRadians ||
                checkedPlan.Elbow.TurnAngleDegrees != e.TurnAngleDegrees ||
                plan.InletRoute.Points.Count != 2 || plan.OutletRoute.Points.Count != 2 ||
                plan.InletRoute.Points[0] != e.Start || plan.InletRoute.Points[1] != e.EntryTangent ||
                plan.OutletRoute.Points[0] != e.ExitTangent || plan.OutletRoute.Points[1] != e.End)
                throw new ArgumentException("弯头方案与验证结果不一致。");

            var g = new PipelineElbowBuildGeometry
            {
                Start = Convert(e.EntryTangent), End = Convert(e.ExitTangent), Center = Convert(e.Center),
                Radius = Meters(e.RadiusMm), Inner = Meters(plan.InsideDiameterMm) / 2,
                Outer = Meters(plan.OutsideDiameterMm) / 2, Wall = Meters(plan.WallThicknessMm),
                ArcLength = Meters(e.ArcLengthMm),
                TurnAngleRadians = e.TurnAngleRadians, TurnAngleDegrees = e.TurnAngleDegrees
            };
            double coordinateScale = Math.Max(g.Center.Length(), Math.Max(g.Start.Length(), g.End.Length()));
            g.PositionTolerance = Math.Max(g.Radius * 1e-8, coordinateScale * 3.552713678800501e-15);
            g.RadiusTolerance = Math.Min(g.Outer * 1e-6, g.Wall / 4);
            if (g.Inner <= 0 || g.Outer <= g.Inner || g.Wall <= 0 || g.Radius <= g.Outer ||
                g.PositionTolerance <= 0 || g.RadiusTolerance <= 0 ||
                g.PositionTolerance >= Math.Min(g.Inner, g.Wall) / 4 ||
                g.RadiusTolerance >= g.Wall / 2)
                throw new ArithmeticException("米单位下尺寸或坐标精度不足，无法可靠校验弯头。");
            Vector a = g.Start - g.Center, b = g.End - g.Center;
            if (!Near(a.Length(), g.Radius, g.PositionTolerance) || !Near(b.Length(), g.Radius, g.PositionTolerance))
                throw new ArithmeticException("圆心至端点的半径不一致。");
            Vector u = a.Unit(), v = b.Unit();
            if (Math.Abs(u.Dot(v) - Math.Cos(g.TurnAngleRadians)) > 1e-9)
                throw new ArithmeticException("弧线端点与转向角不一致。");
            g.Axis = u.Cross(v).Unit();
            g.StartTangent = g.Axis.Cross(u).Unit();
            g.EndTangent = g.Axis.Cross(v).Unit();
            g.Mid = g.Center + (u + v).Unit() * g.Radius;
            if (!Near((g.Mid - g.Center).Length(), g.Radius, g.PositionTolerance) ||
                !Near(g.ArcLength, g.Radius * g.TurnAngleRadians, g.PositionTolerance))
                throw new ArithmeticException("圆弧中点或弧长精度不足。");
            // 使用壁厚乘半径和，避免直接相减两个相近的平方。
            g.Volume = (g.Radius * g.Wall) * (g.Outer + g.Inner) * (Math.PI * g.TurnAngleRadians);
            PipePoint3D.RequireFinite(g.Volume, "理论体积");
            if (g.Volume <= 0 || g.Volume * 1e-5 <= 0)
                throw new ArithmeticException("理论体积溢出或下溢。");
            return g;
        }

        internal static double Meters(double value)
        {
            PipePoint3D.RequireFinite(value, "毫米数值");
            double result = value / 1000;
            if (value != 0 && result == 0) throw new ArithmeticException("毫米转米下溢。");
            PipePoint3D.RequireFinite(result, "米数值");
            return result;
        }
        internal static Vector Convert(PipePoint3D p) => new Vector(Meters(p.X), Meters(p.Y), Meters(p.Z));
        internal static Vector FromArray(double[]? values, int index)
        {
            if (values == null || values.Length < index + 3) throw new InvalidOperationException("几何参数数组不完整。");
            var v = new Vector(values[index], values[index + 1], values[index + 2]);
            v.Length();
            return v;
        }
        internal static bool Near(double a, double b, double tolerance) =>
            !double.IsNaN(a) && !double.IsInfinity(a) && !double.IsNaN(b) && !double.IsInfinity(b) &&
            !double.IsNaN(tolerance) && !double.IsInfinity(tolerance) && tolerance > 0 && Math.Abs(a - b) <= tolerance;
        internal static bool Near(Vector a, Vector b, double tolerance) => (a - b).Length() <= tolerance;
    }
}

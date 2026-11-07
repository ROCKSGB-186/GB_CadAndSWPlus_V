using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using Vector = GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers.PipelineElbowBuildGeometry.Vector;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    /// <summary>可在独立测试程序调用 Run；不访问 SolidWorks、文件或界面。</summary>
    internal static class PipelineElbowBuildGeometrySimulation
    {
        internal static int Run()
        {
            var random = new Random(9);
            for (int i = 0; i < 1000; i++)
            {
                double a = random.NextDouble() * 2 * Math.PI;
                double b = (random.NextDouble() - 0.5) * 3;
                double c = random.NextDouble() * 2 * Math.PI;
                var u = new Vector(Math.Cos(a) * Math.Cos(b), Math.Sin(a) * Math.Cos(b), Math.Sin(b));
                var t = new Vector(-Math.Sin(a), Math.Cos(a), 0);
                var v = t * Math.Cos(c) + u.Cross(t) * Math.Sin(c);
                var corner = new Vector(random.NextDouble() * 200 - 100, random.NextDouble() * 200 - 100,
                    random.NextDouble() * 200 - 100);
                var start = corner - u * 500;
                var end = corner + v * 500;
                var plan = PipelineThreePiecePlan.Create(new PipeRoute(new[] {
                    Point(start), Point(corner), Point(end) }), 100, 20, 2);
                var g = PipelineElbowBuildGeometry.Create(plan);
                double volume = Math.PI * Math.PI / 2 * 0.1 * 0.002 * (0.01 + 0.008);
                if (Math.Abs((g.Mid - g.Center).Length() - 0.1) > 1e-10 ||
                    Math.Abs(g.Axis.Dot(u)) > 1e-10 || Math.Abs(g.Axis.Dot(v)) > 1e-10 ||
                    (g.StartTangent - u).Length() > 1e-10 || (g.EndTangent - v).Length() > 1e-10 ||
                    Math.Abs(g.Volume - volume) > 1e-14)
                    throw new InvalidOperationException("随机三维方向数学模拟失败。");
            }

            var route = new PipeRoute(new[] { new PipePoint3D(0, 0, 0),
                new PipePoint3D(500, 0, 0), new PipePoint3D(500, 500, 0) });
            bool rejected = false;
            try { PipelineThreePiecePlan.Create(route, 10, 20, 2); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("退化半径未被拒绝。");
            rejected = false;
            try { PipelineElbowBuildGeometry.Meters(double.Epsilon); }
            catch (ArithmeticException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("单位转换下溢未被拒绝。");
            return 1000;
        }

        private static PipePoint3D Point(Vector p) => new PipePoint3D(p.X, p.Y, p.Z);
    }
}

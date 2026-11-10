using System;
using System.Collections.Generic;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;

// 纯数学回归，不加载或连接 SolidWorks。
internal static class SelfCollisionTests
{
    private static int passed, failed;
    private static PipePoint3D P(double x, double y, double z = 0) => new PipePoint3D(x, y, z);
    private static PipelineMultiSegmentPlan Plan(double radius, double diameter, params PipePoint3D[] points) =>
        PipelineMultiSegmentPlan.Create(new PipeRoute(points), radius, diameter, diameter / 10);
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("通过 " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("失败 " + name + "：" + ex); }
    }
    private static void Reject(PipelineMultiSegmentPlan plan)
    {
        try { PipelineRouteSelfCollision.Validate(plan); }
        catch (InvalidOperationException) { return; }
        catch (ArithmeticException) { return; }
        throw new Exception("危险或无法判定的路线未被拒绝");
    }
    private static bool HasSampleOverlap(PipelineMultiSegmentPlan plan, HashSet<string> kinds)
    {
        var pieces = new List<List<PipePoint3D>>();
        for (int i = 0; i < plan.Straights.Count; i++)
        {
            PipePoint3D a = plan.Straights[i].Points[0], b = plan.Straights[i].Points[1];
            var line = new List<PipePoint3D>();
            for (int j = 0; j <= 30; j++)
            {
                double t = j / 30.0;
                line.Add(P(a.X + (b.X-a.X)*t, a.Y + (b.Y-a.Y)*t, a.Z + (b.Z-a.Z)*t));
            }
            pieces.Add(line);
            if (i == plan.Elbows.Count) continue;
            var g = PipelineElbowBuildGeometry.Create(plan.ElbowPlans[i]);
            var u = g.Start - g.Center;
            var v = g.Axis.Cross(u);
            var arc = new List<PipePoint3D>();
            for (int j = 0; j <= 30; j++)
            {
                double angle = g.TurnAngleRadians * j / 30.0;
                var p = g.Center + u * Math.Cos(angle) + v * Math.Sin(angle);
                arc.Add(P(p.X*1000, p.Y*1000, p.Z*1000));
            }
            pieces.Add(arc);
        }
        double d2 = plan.OutsideDiameterMm * plan.OutsideDiameterMm * 0.99;
        bool found = false;
        for (int i = 0; i < pieces.Count; i++)
            for (int j = i + 2; j < pieces.Count; j++)
            {
                bool pairFound = false;
                foreach (var a in pieces[i])
                {
                    foreach (var b in pieces[j])
                    {
                        double x=a.X-b.X, y=a.Y-b.Y, z=a.Z-b.Z;
                        if (x*x+y*y+z*z < d2) { pairFound = true; break; }
                    }
                    if (pairFound) break;
                }
                if (pairFound)
                {
                    found = true;
                    kinds.Add(i%2==0 && j%2==0 ? "直管-直管" : i%2==1 && j%2==1 ? "弯头-弯头" : "直管-弯头");
                }
            }
        return found;
    }
    private static int Main()
    {
        Run("已验证平面路线", () => PipelineRouteSelfCollision.Validate(
            Plan(100, 100, P(0,0), P(1000,0), P(1000,1000), P(2000,1000))));
        Run("已验证非共面路线", () => PipelineRouteSelfCollision.Validate(
            Plan(100, 100, P(0,0,0), P(1000,0,0), P(1000,1000,0), P(1000,1000,1000))));
        Run("平移路线", () => PipelineRouteSelfCollision.Validate(
            Plan(100, 100, P(5000,5000), P(6000,5000), P(6000,6000), P(7000,6000))));
        Run("跨段中心线相交", () => Reject(
            Plan(100,100, P(0,0), P(1000,0), P(1000,1000), P(500,1000), P(500,-500))));
        Run("平行管外径重叠", () => Reject(
            Plan(40,60, P(0,0), P(1000,0), P(1000,200), P(0,200), P(0,50), P(1000,50))));
        Run("闭合路线自交", () => Reject(
            Plan(40,60, P(0,0), P(1000,0), P(1000,1000), P(0,1000), P(0,-100))));
        Run("空间交叉投影但垂直分离", () => PipelineRouteSelfCollision.Validate(
            Plan(100,100, P(0,0,0), P(1000,0,0), P(1000,1000,0), P(1000,1000,1000), P(500,1000,1000), P(500,-500,1000))));
        double q = 1000 * Math.Sqrt(0.5);
        Run("四十五度转弯", () => PipelineRouteSelfCollision.Validate(
            Plan(100,20, P(0,0), P(1000,0), P(1000+q,q), P(1000+q,q+1000))));
        Run("一百三十五度转弯", () => PipelineRouteSelfCollision.Validate(
            Plan(100,20, P(0,0), P(1000,0), P(1000-q,q), P(1000-q,q+1000))));
        Run("精确外径接触", () => Reject(
            Plan(40,60, P(0,0), P(1000,0), P(1000,200), P(0,200), P(0,60), P(1000,60))));
        Run("纯直线合并", () => PipelineRouteSelfCollision.Validate(
            Plan(100,100, P(0,0), P(1000,0), P(2000,0), P(3000,0))));
        Run("反向坐标九十度", () => PipelineRouteSelfCollision.Validate(
            Plan(100,100, P(0,0), P(-1000,0), P(-1000,-1000), P(-2000,-1000))));
        Run("竖直平面九十度", () => PipelineRouteSelfCollision.Validate(
            Plan(100,100, P(0,0,0), P(0,0,1000), P(0,1000,1000), P(0,1000,2000))));
        Run("斜向九十度", () => PipelineRouteSelfCollision.Validate(
            Plan(100,100, P(0,0), P(600,800), P(-200,1400), P(400,2200))));
        Run("斜向中心线相交", () => Reject(
            Plan(100,100, P(0,0), P(600,800), P(-200,1400), P(-500,1000), P(700,100))));
        Run("合法紧凑S弯", () => PipelineRouteSelfCollision.Validate(
            Plan(100,20, P(0,0), P(1000,0), P(1000,220), P(2000,220))));
        Run("相距较远的斜直管", () => PipelineRouteSelfCollision.Validate(
            Plan(50,20, P(0,0,0), P(1000,1000,0), P(1000,1000,1000),
                P(-500,0,1000), P(-500,0,0), P(1000,1500,0))));
        Run("近距但分离的斜向平行线", () => PipelineRouteSelfCollision.Validate(
            Plan(50,20, P(0,0,0), P(1000,1000,0), P(1000,1000,1000),
                P(-100,-60,1000), P(-100,-60,0), P(1000,1040,0))));
        for (int i = 0; i < 4; i++)
        {
            int rotation = i;
            Func<PipePoint3D, PipePoint3D> transform = p =>
            {
                double x = p.X, y = p.Y;
                for (int n = 0; n < rotation; n++) { double next = -y; y = x; x = next; }
                return P(x + 5000, y - 3000, 1000);
            };
            Run("交叉旋转和平移 " + i, () => Reject(Plan(100,100,
                transform(P(0,0)), transform(P(1000,0)), transform(P(1000,1000)),
                transform(P(500,1000)), transform(P(500,-500)))));
            Run("正常路线旋转和平移 " + i, () => PipelineRouteSelfCollision.Validate(Plan(100,100,
                transform(P(0,0)), transform(P(1000,0)), transform(P(1000,1000)),
                transform(P(2000,1000)))));
        }
        Run("随机路线采样交叉核对", () =>
        {
            var random = new Random(12881);
            int sampledRisks = 0;
            var kinds = new HashSet<string>();
            for (int k = 0; k < 150; k++)
            {
                var points = new List<PipePoint3D> { P(0,0) };
                int dx = 1, dy = 0, x = 0, y = 0;
                for (int i = 0; i < 6; i++)
                {
                    int length = 50 * (1 + random.Next(3));
                    x += dx * length; y += dy * length; points.Add(P(x,y));
                    int sign = random.Next(2) == 0 ? -1 : 1;
                    int next = -sign * dy; dy = sign * dx; dx = next;
                }
                var plan = Plan(10,10,points.ToArray());
                if (!HasSampleOverlap(plan, kinds)) continue;
                sampledRisks++;
                Reject(plan);
            }
            if (sampledRisks < 10) throw new Exception("随机样本中风险数量不足：" + sampledRisks);
            Console.WriteLine("随机采样发现并拦截风险路线：" + sampledRisks + "；类型：" + string.Join("、", kinds));
            if (kinds.Count != 3) throw new Exception("随机采样未覆盖全部管件组合");
        });
        Run("三维随机路线采样核对", () =>
        {
            var random = new Random(29811);
            int sampledRisks = 0;
            var kinds = new HashSet<string>();
            for (int k = 0; k < 250; k++)
            {
                var points = new List<PipePoint3D> { P(0,0,0) };
                int axis=0, sign=1;
                var coords = new int[3];
                for (int i=0; i<8; i++)
                {
                    coords[axis] += sign * 50 * (1 + random.Next(3));
                    points.Add(P(coords[0],coords[1],coords[2]));
                    axis = (axis + 1 + random.Next(2)) % 3;
                    sign = random.Next(2) == 0 ? -1 : 1;
                }
                var plan = Plan(10,10,points.ToArray());
                if (!HasSampleOverlap(plan, kinds)) continue;
                sampledRisks++;
                Reject(plan);
            }
            if (sampledRisks < 10 || kinds.Count != 3)
                throw new Exception("三维样本风险或覆盖不足：" + sampledRisks + "；类型：" + string.Join("、", kinds));
            Console.WriteLine("三维随机采样发现并拦截风险路线：" + sampledRisks + "；类型：" + string.Join("、", kinds));
        });
        Run("最大规模路线", () =>
        {
            var points = new List<PipePoint3D> { P(0,0) };
            double x=0, y=0;
            for (int i=0; i<128; i++) { if (i%2==0) x+=1000; else y+=1000; points.Add(P(x,y)); }
            var plan = Plan(100,100,points.ToArray());
            var timer = System.Diagnostics.Stopwatch.StartNew();
            PipelineRouteSelfCollision.Validate(plan);
            Console.WriteLine("最大规模预检耗时：" + timer.ElapsedMilliseconds + "ms");
        });
        Run("超计算预算阻止", () =>
        {
            var points = new List<PipePoint3D> { P(0,0) };
            double x=0, y=0;
            for (int i=0; i<129; i++) { if (i%2==0) x+=1000; else y+=1000; points.Add(P(x,y)); }
            Reject(Plan(100,100,points.ToArray()));
        });
        Console.WriteLine("结果：通过 " + passed + "；失败 " + failed);
        return failed == 0 ? 0 : 1;
    }
}

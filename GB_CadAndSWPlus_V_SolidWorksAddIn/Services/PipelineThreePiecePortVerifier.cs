using System;
using System.Collections.Generic;
using System.Globalization;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Geometry = GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers.PipelineElbowBuildGeometry;
using Vector = GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers.PipelineElbowBuildGeometry.Vector;
using Straight = GB_CadAndSWPlus_V.SolidWorksAddIn.Services.PipelineStraightModelBuilder;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// 只读检查入直管—弯头、弯头—出直管的组件实体端口；components 顺序为入直管、弯头、出直管。
    /// 仅支持零件与装配根坐标之间均为单位变换；身份、配置、变换和文档状态由调用者及 guard 保证。
    /// 不检查完整零件形状、贯通、全局干涉或公差以内的间隙/重叠，也不修改、激活或切换文档。
    /// GetBodies3 返回体的几何坐标系及装配级修改覆盖范围尚未充分确认，因此 complete 始终为 false。
    /// </summary>
    internal static class PipelineThreePiecePortVerifier
    {
        private sealed class Circle
        {
            internal Vector Center, Normal;
            internal double Radius;
            internal Edge Edge = null!;
            internal Curve Curve = null!;
        }

        private sealed class Port
        {
            internal Vector Normal;
            internal Circle Inner = null!, Outer = null!;
        }

        internal static string Verify(SldWorks app, Component2[] components, PipelineThreePiecePlan plan,
            Action guard, out bool complete)
        {
            complete = false;
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (components == null) throw new ArgumentNullException(nameof(components));
            if (components.Length != 3) throw new ArgumentException("必须按入直管、弯头、出直管顺序提供三个组件。", nameof(components));
            if (guard == null) throw new ArgumentNullException(nameof(guard));
            var requested = (Component2[])components.Clone();
            for (int i = 0; i < requested.Length; i++)
                if (requested[i] == null) throw new ArgumentException("组件数组包含空对象。", nameof(components));
            Geometry g = Geometry.Create(plan);
            // 沿用弯头构建器的米单位位置、半径精度；方向再限制到 1e-8 弧度量级。
            double angleTolerance = Math.Min(1e-8, g.PositionTolerance / g.Outer);
            if (angleTolerance <= 0 || double.IsNaN(angleTolerance) || double.IsInfinity(angleTolerance))
                throw new ArithmeticException("方向精度不足，无法核验端口。");
            guard();
            string stage = "读取三个组件实体";
            try
            {
                Body2[] bodies = new Body2[3];
                string[] bodyKinds = new string[3];
                for (int i = 0; i < 3; i++) bodies[i] = ReadBody(requested[i], guard, out bodyKinds[i]);
                stage = "入直管端口";
                Port inlet = FindPort(bodies[0], g.Start, g.StartTangent, g, angleTolerance, guard);
                stage = "弯头入口端口";
                Port elbowIn = FindPort(bodies[1], g.Start, g.StartTangent * -1, g, angleTolerance, guard);
                stage = "入直管与弯头配对";
                VerifyPair(inlet, elbowIn, g, angleTolerance);
                stage = "弯头出口端口";
                Port elbowOut = FindPort(bodies[1], g.End, g.EndTangent, g, angleTolerance, guard);
                stage = "出直管端口";
                Port outlet = FindPort(bodies[2], g.End, g.EndTangent * -1, g, angleTolerance, guard);
                stage = "弯头与出直管配对";
                VerifyPair(elbowOut, outlet, g, angleTolerance);
                guard();
                // 目前只能说明假设这些参数处于统一坐标系时数值吻合，不能据此证明装配体端口。
                return string.Format(CultureInfo.InvariantCulture,
                    "装配体端口结论不确定：GetBodies3 返回体的几何坐标系及装配级修改的完整覆盖范围尚未得到充分确认；" +
                    "不得将本次数值比较视为核验通过。若参数处于同一坐标系，则两对端口的圆边、圆心、半径和法向数值匹配；仅支持单位变换。" +
                    "位置及端点容差={0:G17}米，半径容差={1:G17}米，方向正弦容差={2:G17}；" +
                    "圆周长容差为max(实际周长×1e-8,位置容差×2π)。组件实体类型依次为{3}、{4}、{5}。" +
                    "不证明全局无干涉、内孔贯通或容差内无间隙/重叠。",
                    g.PositionTolerance, g.RadiusTolerance, angleTolerance, bodyKinds[0], bodyKinds[1], bodyKinds[2]);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                throw new InvalidOperationException("三件装配体端口结论不确定；阶段=" + stage +
                    "；GetBodies3 几何坐标系及装配级修改覆盖范围尚未充分确认，不能判定核验通过；原因=" + ex.Message, ex);
            }
        }

        private static Body2 ReadBody(Component2 component, Action guard, out string bodyKind)
        {
            // 2022 GetBodies3 的 user body 表示装配级修改后的组件实体，不得回退到零件文档体。
            object? info = null;
            object value = Call(guard, () => component.GetBodies3((int)swBodyType_e.swSolidBody, out info));
            var bodies = value as Array;
            var kinds = info as Array;
            if (bodies == null || bodies.Rank != 1 || bodies.Length != 1)
                throw new InvalidOperationException("组件必须恰有一个实体，无法唯一确定端口所属实体。");
            if (kinds == null || kinds.Rank != 1 || kinds.Length != bodies.Length ||
                !(kinds.GetValue(kinds.GetLowerBound(0)) is int kind))
                throw new InvalidOperationException("组件实体信息不是与实体一一对应的有效整数数组。");
            if (kind == (int)swBodyInfo_e.swNormalBody_e) bodyKind = "普通体";
            else if (kind == (int)swBodyInfo_e.swUserBody_e) bodyKind = "装配级修改体";
            else throw new InvalidOperationException("组件实体信息含未知类型，无法确认实体来源。");
            var body = bodies.GetValue(bodies.GetLowerBound(0)) as Body2 ??
                throw new InvalidOperationException("无法读取组件实体接口。");
            if (Call(guard, () => body.GetType()) != (int)swBodyType_e.swSolidBody)
                throw new InvalidOperationException("组件端口所属体不是实体。");
            return body;
        }

        private static Port FindPort(Body2 body, Vector expectedCenter, Vector outward, Geometry g,
            double angleTolerance, Action guard)
        {
            List<object> faces = Straight.ReadComObjects(Call(guard, () => body.GetFaces()), "实体面");
            Port? found = null;
            foreach (object item in faces)
            {
                var face = item as Face2 ?? throw new InvalidOperationException("无法读取实体面。");
                var surface = Call(guard, () => face.GetSurface()) as Surface ?? throw new InvalidOperationException("无法读取曲面。");
                if (!Call(guard, () => surface.IsPlane())) continue;
                List<object> edges = Straight.ReadComObjects(Call(guard, () => face.GetEdges()), "端面边界");
                var circles = new List<Circle>();
                bool candidate = false;
                foreach (object edgeObject in edges)
                {
                    var edge = edgeObject as Edge ?? throw new InvalidOperationException("无法读取端面边界。");
                    var curve = Call(guard, () => edge.GetCurve()) as Curve ?? throw new InvalidOperationException("无法读取端面边界曲线。");
                    if (!Call(guard, () => curve.IsCircle())) continue;
                    var p = Call(guard, () => curve.CircleParams) as double[];
                    if (p == null || p.Length < 7) throw new InvalidOperationException("圆边参数不完整。");
                    for (int i = 0; i < 7; i++) PipePoint3D.RequireFinite(p[i], "圆边参数");
                    var circle = new Circle { Center = Geometry.FromArray(p, 0), Normal = Geometry.FromArray(p, 3).Unit(), Radius = p[6], Edge = edge, Curve = curve };
                    circles.Add(circle);
                    if (Geometry.Near(circle.Center, expectedCenter, g.PositionTolerance)) candidate = true;
                }
                if (!candidate) continue;
                if (found != null) throw new InvalidOperationException("目标位置存在多个候选端面，端口不唯一。");
                // 真实的环形端面必须恰有两条边，且两条完整圆边属于同一个面。
                if (edges.Count != 2 || circles.Count != 2)
                    throw new InvalidOperationException("候选端面不是由两条完整圆边构成的环面。");
                Vector normal = Geometry.FromArray(Call(guard, () => face.Normal) as double[], 0).Unit();
                RequireDirection(normal, outward, angleTolerance, true, "端面外法向与预期方向不一致");
                var port = new Port { Normal = normal };
                foreach (Circle c in circles)
                {
                    if (!Geometry.Near(c.Center, expectedCenter, g.PositionTolerance))
                        throw new InvalidOperationException("同一端面的内外圆心与目标位置不一致。");
                    RequireDirection(c.Normal, normal, angleTolerance, false, "圆边法向与端面不一致");
                    if (Geometry.Near(c.Radius, g.Inner, g.RadiusTolerance))
                    {
                        if (port.Inner != null) throw new InvalidOperationException("内圆边不唯一。");
                        port.Inner = c;
                    }
                    else if (Geometry.Near(c.Radius, g.Outer, g.RadiusTolerance))
                    {
                        if (port.Outer != null) throw new InvalidOperationException("外圆边不唯一。");
                        port.Outer = c;
                    }
                    else throw new InvalidOperationException("圆边半径与方案内外径不一致。");
                    VerifyCompleteCircle(c, g.PositionTolerance, guard);
                }
                if (port.Inner == null || port.Outer == null ||
                    !Geometry.Near(port.Inner.Center, port.Outer.Center, g.PositionTolerance))
                    throw new InvalidOperationException("端面内外圆不同心或缺失。");
                found = port;
            }
            return found ?? throw new InvalidOperationException("目标位置未找到可确认的真实端面。");
        }

        private static void VerifyCompleteCircle(Circle circle, double positionTolerance, Action guard)
        {
            if (circle.Radius <= 0) throw new InvalidOperationException("圆边半径无效。");
            CurveParamData data = Call(guard, () => circle.Edge.GetCurveParams3()) ??
                throw new InvalidOperationException("无法读取圆边裁剪参数。");
            double min = Call(guard, () => data.UMinValue), max = Call(guard, () => data.UMaxValue);
            PipePoint3D.RequireFinite(min, "圆边起始参数");
            PipePoint3D.RequireFinite(max, "圆边终止参数");
            if (max <= min) throw new InvalidOperationException("圆边裁剪参数无效。");
            double circumference = 2 * Math.PI * circle.Radius;
            double lengthTolerance = Math.Max(circumference * 1e-8, positionTolerance * 2 * Math.PI);
            PipePoint3D.RequireFinite(circumference, "圆周长");
            PipePoint3D.RequireFinite(lengthTolerance, "圆周长容差");
            if (circumference <= 0 || lengthTolerance <= 0 || lengthTolerance >= circumference / 2 ||
                !Geometry.Near(Call(guard, () => circle.Curve.GetLength3(min, max)), circumference, lengthTolerance))
                throw new InvalidOperationException("裁剪圆边长度不是可确认的完整圆周。");
            Vector first = Geometry.FromArray(Call(guard, () => data.StartPoint) as double[], 0);
            Vector last = Geometry.FromArray(Call(guard, () => data.EndPoint) as double[], 0);
            CheckEndpoint(first, circle, positionTolerance);
            CheckEndpoint(last, circle, positionTolerance);
            if (!Geometry.Near(first, last, positionTolerance))
                throw new InvalidOperationException("圆边裁剪端点未闭合。");
        }

        private static void CheckEndpoint(Vector point, Circle circle, double tolerance)
        {
            Vector offset = point - circle.Center;
            if (!Geometry.Near(offset.Length(), circle.Radius, tolerance) || Math.Abs(offset.Dot(circle.Normal)) > tolerance)
                throw new InvalidOperationException("圆边裁剪端点不在圆周平面上。");
        }

        private static void VerifyPair(Port a, Port b, Geometry g, double angleTolerance)
        {
            RequireDirection(a.Normal, b.Normal * -1, angleTolerance, true, "配对端面外法向不相反");
            if (!Geometry.Near(a.Inner.Center, b.Inner.Center, g.PositionTolerance) ||
                !Geometry.Near(a.Outer.Center, b.Outer.Center, g.PositionTolerance) ||
                !Geometry.Near(a.Inner.Radius, b.Inner.Radius, g.RadiusTolerance) ||
                !Geometry.Near(a.Outer.Radius, b.Outer.Radius, g.RadiusTolerance))
                throw new InvalidOperationException("配对端口存在超差间隙、错轴或内外径差。");
        }

        private static void RequireDirection(Vector actual, Vector expected, double tolerance, bool directed, string error)
        {
            double dot = actual.Dot(expected);
            PipePoint3D.RequireFinite(dot, "方向点积");
            if ((directed && dot <= 0) || actual.Cross(expected).Length() > tolerance)
                throw new InvalidOperationException(error + "。");
        }

        // 每个关键 COM 读取前后都交由调用方检查；即使读取抛异常也执行后检查。
        private static T Call<T>(Action guard, Func<T> read)
        {
            guard();
            Exception? failure = null;
            try { return read(); }
            catch (Exception ex) when (IsExpected(ex)) { failure = ex; throw; }
            finally
            {
                try { guard(); }
                catch (Exception ex) when (IsExpected(ex))
                {
                    if (failure != null)
                        throw new AggregateException("实体读取与后置状态检查均失败。", failure, ex);
                    throw;
                }
            }
        }

        private static bool IsExpected(Exception ex) =>
            ex is InvalidOperationException || ex is ArgumentException || ex is ArithmeticException ||
            ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException ||
            ex is System.Runtime.InteropServices.InvalidComObjectException || ex is System.IO.IOException ||
            ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is AggregateException;
    }
}

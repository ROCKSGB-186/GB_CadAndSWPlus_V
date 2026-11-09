using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Geometry = GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers.PipelineElbowBuildGeometry;
using Vector = GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers.PipelineElbowBuildGeometry.Vector;
using Straight = GB_CadAndSWPlus_V.SolidWorksAddIn.Services.PipelineStraightModelBuilder;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class PipelineElbowModelResult
    {
        internal PipelineElbowModelResult(ModelDoc2 document, string title, Feature feature,
            Feature pathFeature, SketchSegment segment)
        { Document = document; Title = title; Feature = feature; PathFeature = pathFeature; Segment = segment; }
        public ModelDoc2 Document { get; }
        public string Title { get; }
        public Feature Feature { get; }
        public Feature PathFeature { get; }
        public SketchSegment Segment { get; }
    }

    /// <summary>已保存后复验失败；观察路径可能不可用或与调用方指定的保存路径不同。</summary>
    internal sealed class PipelineElbowModelRevalidationException : Exception
    {
        internal PipelineElbowModelRevalidationException(string stage, string? observedPath, Exception inner)
            : base("弯头文件已保存，但后续复验失败；阶段=" + stage + "；原因=" + inner.Message +
                "。文件或零件可能异常；未自动删除文件、关闭零件或回滚，请用户检查。" +
                "\n复验时观察到的路径：" + (string.IsNullOrWhiteSpace(observedPath) ? "不可用，请以选择的保存路径为准。" : observedPath), inner)
        { Stage = stage; ObservedPath = string.IsNullOrWhiteSpace(observedPath) ? null : observedPath; }
        public string Stage { get; }
        public string? ObservedPath { get; }
    }

    internal sealed class PipelineElbowModelBuildException : Exception
    {
        internal PipelineElbowModelBuildException(string stage, string? title, bool newDocumentAttempted, Exception inner)
            : base("弯头生成失败；阶段=" + stage + "；原因=" + inner.Message +
                (newDocumentAttempted
                    ? title == null ? "。可能存在未保存的新文档；未自动关闭或回滚。"
                        : "。测试零件“" + title + "”可能不完整；未自动关闭或回滚。"
                    : "。尚未尝试新建零件。"), inner)
        { Stage = stage; DocumentTitle = title; }
        public string Stage { get; }
        public string? DocumentTitle { get; }
    }

    /// <summary>仅新建独立未保存零件；失败时保留现场，不操作原装配体。</summary>
    internal static class PipelineElbowModelBuilder
    {
        public static PipelineElbowModelResult Build(SldWorks application, IntPtr mainWindowHandle,
            string partTemplatePath, PipelineThreePiecePlan plan)
        {
            return Build(application, mainWindowHandle, partTemplatePath, plan, null);
        }

        internal static PipelineElbowModelResult Build(SldWorks application, IntPtr mainWindowHandle,
            string partTemplatePath, PipelineThreePiecePlan plan,
            Action<ModelDoc2, IReadOnlyList<object>>? verifyNewDocument)
        {
            string stage = "输入和线程预检";
            string? title = null;
            bool newDocumentAttempted = false;
            try
            {
                if (application == null) throw new ArgumentNullException(nameof(application));
                Straight.RequireUiThread(mainWindowHandle);
                if (string.IsNullOrWhiteSpace(partTemplatePath) || !Straight.IsFullyQualifiedPath(partTemplatePath) ||
                    !string.Equals(Path.GetExtension(partTemplatePath), ".prtdot", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(partTemplatePath))
                    throw new ArgumentException("必须指定存在的零件模板 .prtdot 绝对路径。");
                Geometry g = Geometry.Create(plan);
                stage = "确认主窗口和命令状态";
                var frame = application.Frame() as Frame;
                if (frame == null || new IntPtr(frame.GetHWndx64()) != mainWindowHandle)
                    throw new InvalidOperationException("传入的窗口不是该 SolidWorks 实例的主窗口。");
                application.GetRunningCommandInfo(out int command, out string commandTitle, out bool activeCommand);
                if (activeCommand) throw new InvalidOperationException("存在活动命令，不能新建零件。");

                stage = "记录已有文档并新建零件";
                List<object> previous = Straight.ReadComObjects(application.GetDocuments(), "已有文档列表");
                SolidWorksFileLogger.Info("开始新建独立未保存弯头零件。");
                newDocumentAttempted = true;
                var document = application.NewDocument(partTemplatePath, 0, 0, 0) as ModelDoc2;
                if (document == null) throw new InvalidOperationException("新建文档未返回零件对象。");
                // 多段试验在任何文档写操作前，额外核对此前观察到的全部文档身份。
                stage = "校验新文档身份";
                verifyNewDocument?.Invoke(document, previous);
                stage = "记录已有文档并新建零件";
                foreach (object old in previous)
                    if (application.IsSame(old, document) != (int)swObjectEquality.swObjectNotSame)
                        throw new InvalidOperationException("无法确认返回的文档是本次新建的独立零件。");
                title = document.GetTitle();
                RequireTarget(application, document);
                if (string.IsNullOrWhiteSpace(title) || document.GetType() != (int)swDocumentTypes_e.swDocPART ||
                    !string.IsNullOrEmpty(document.GetPathName()))
                    throw new InvalidOperationException("新文档不是有标题的未保存零件。");
                var part = document as PartDoc ?? throw new InvalidOperationException("零件接口不可用。");
                var manager = document.SketchManager ?? throw new InvalidOperationException("草图管理器不可用。");
                var features = document.FeatureManager ?? throw new InvalidOperationException("特征管理器不可用。");
                if (manager.ActiveSketch != null ||
                    Straight.ReadComObjects(part.GetBodies2((int)swBodyType_e.swSolidBody, false), "初始实体").Count != 0 ||
                    Straight.ReadComObjects(part.GetBodies2((int)swBodyType_e.swSheetBody, false), "初始曲面").Count != 0)
                    throw new InvalidOperationException("零件模板包含活动草图、实体或曲面。");

                stage = "创建并校验三维圆弧";
                RequireTarget(application, document);
                List<object> oldFeatures = Straight.ReadFeatures(document);
                RequireTarget(application, document);
                document.ClearSelection2(true);
                RequireTarget(application, document);
                manager.Insert3DSketch(true);
                RequireTarget(application, document);
                Sketch? sketch = manager.ActiveSketch;
                if (sketch == null || !sketch.Is3D()) throw new InvalidOperationException("未进入三维草图。");
                RequireTarget(application, document);
                SketchSegment segment = manager.Create3PointArc(g.Start.X, g.Start.Y, g.Start.Z,
                    g.End.X, g.End.Y, g.End.Z, g.Mid.X, g.Mid.Y, g.Mid.Z);
                if (segment == null || application.IsSame(segment.GetSketch(), sketch) != (int)swObjectEquality.swObjectSame)
                    throw new InvalidOperationException("圆弧未属于本次三维草图。");
                VerifyArc(segment, g, () => RequireTarget(application, document));
                RequireTarget(application, document);
                manager.Insert3DSketch(true);
                RequireTarget(application, document);
                if (manager.ActiveSketch != null) throw new InvalidOperationException("未退出三维草图。");
                Feature path = Straight.FindNewPath(application, document, oldFeatures, segment);
                var pathSketch = path.GetSpecificFeature2() as Sketch;
                if (pathSketch == null || !pathSketch.Is3D()) throw new InvalidOperationException("路径不是三维草图。");
                RequireTarget(application, document);
                VerifyArc(segment, g, () => RequireTarget(application, document));
                SolidWorksFileLogger.Info("弯头三维圆弧端点、圆心、半径和弧长校验通过。");

                stage = "创建薄壁圆形扫掠";
                RequireTarget(application, document);
                document.ClearSelection2(true);
                RequireTarget(application, document);
                if (!path.Select2(false, 4)) throw new InvalidOperationException("无法选择三维草图路径。");
                RequireTarget(application, document);
                var sweep = features.CreateDefinition((int)swFeatureNameID_e.swFmSweep) as SweepFeatureData ??
                    throw new InvalidOperationException("无法创建扫掠参数。");
                sweep.CircularProfile = true;
                sweep.CircularProfileDiameter = g.Inner * 2;
                sweep.ThinFeature = true;
                sweep.ThinWallType = (short)swThinWallType_e.swThinWallOneDirection;
                sweep.SetWallThickness(true, g.Wall);
                RequireTarget(application, document);
                Feature feature = features.CreateFeature(sweep);
                if (feature == null) throw new InvalidOperationException("薄壁扫掠创建失败。");

                stage = "重建和校验实体";
                RequireTarget(application, document);
                if (!document.EditRebuild3()) throw new InvalidOperationException("零件重建失败。");
                RequireTarget(application, document);
                int error = feature.GetErrorCode2(out bool warning);
                if (error != 0 || warning) throw new InvalidOperationException("扫掠特征存在错误或警告；错误码=" + error);
                RequireTarget(application, document);
                VerifyArc(segment, g, () => RequireTarget(application, document));
                VerifyBody(application, document, part, g, () => RequireTarget(application, document));
                RequireTarget(application, document);
                if (!string.IsNullOrEmpty(document.GetPathName()))
                    throw new InvalidOperationException("零件已被外部操作保存，停止报告成功。");
                SolidWorksFileLogger.Info("独立弯头重建完成：实体、环面、端面边界及体积校验通过；零件未保存。");
                return new PipelineElbowModelResult(document, title, feature, path, segment);
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidComObjectException ||
                ex is InvalidOperationException || ex is ArgumentException || ex is ArithmeticException ||
                ex is NotSupportedException || ex is IOException || ex is UnauthorizedAccessException ||
                ex is System.Security.SecurityException)
            {
                SolidWorksFileLogger.Error("独立弯头生成失败；阶段=" + stage + "；异常类型=" +
                    ex.GetType().Name + "；未执行关闭或回滚。");
                throw new PipelineElbowModelBuildException(stage, title, newDocumentAttempted, ex);
            }
        }

        /// <summary>仅复验保存后的原零件，不修改、关闭或重新保存文件。</summary>
        internal static void Revalidate(SldWorks application, PipelineElbowModelResult result,
            PipelineThreePiecePlan plan, string expectedPath)
        {
            string stage = "输入和线程预检";
            string? observedPath = null;
            try
            {
                if (application == null) throw new ArgumentNullException(nameof(application));
                if (result == null) throw new ArgumentNullException(nameof(result));
                if (string.IsNullOrWhiteSpace(expectedPath))
                    throw new ArgumentException("保存目标路径不可用。", nameof(expectedPath));
                Geometry g = Geometry.Create(plan);
                var frame = application.Frame() as Frame ??
                    throw new InvalidOperationException("SolidWorks 主窗口不可用。");
                Straight.RequireUiThread(new IntPtr(frame.GetHWndx64()));
                ModelDoc2 document = result.Document;
                Straight.RequireActive(application, document);
                string path = document.GetPathName();
                observedPath = path;
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("原零件没有保存路径。");
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("复验零件路径与选择的保存路径不一致。");

                // 整个 COM 复验过程始终使用同一已保存路径与活动零件。
                Action guard = () => RequireSavedTarget(application, document, path);
                guard();
                stage = "检查原始特征和路径";
                bool foundFeature = false, foundPath = false;
                Feature? current = document.FirstFeature() as Feature;
                int count = 0;
                while (current != null)
                {
                    guard();
                    if (++count > 10000) throw new InvalidOperationException("特征树过长或存在循环。");
                    if (application.IsSame(current, result.Feature) == (int)swObjectEquality.swObjectSame)
                        foundFeature = true;
                    if (application.IsSame(current, result.PathFeature) == (int)swObjectEquality.swObjectSame)
                        foundPath = true;
                    guard();
                    current = current.GetNextFeature() as Feature;
                }
                guard();
                if (!foundFeature || !foundPath)
                    throw new InvalidOperationException("原扫掠特征或路径草图已不在该零件中。");
                int error = result.Feature.GetErrorCode2(out bool warning);
                guard();
                if (error != 0 || warning)
                    throw new InvalidOperationException("扫掠特征存在错误或警告；错误码=" + error + "。");
                error = result.PathFeature.GetErrorCode2(out warning);
                guard();
                if (error != 0 || warning)
                    throw new InvalidOperationException("路径草图存在错误或警告；错误码=" + error + "。");
                if (result.PathFeature.GetTypeName2() != "3DProfileFeature")
                    throw new InvalidOperationException("原路径不再是三维草图特征。");
                guard();
                var sketch = result.PathFeature.GetSpecificFeature2() as Sketch;
                if (sketch == null || !sketch.Is3D())
                    throw new InvalidOperationException("原路径不是三维草图。");
                guard();
                var segments = Straight.ReadComObjects(sketch.GetSketchSegments(), "原路径草图线段");
                guard();
                if (segments.Count != 1 ||
                    application.IsSame(segments[0], result.Segment) != (int)swObjectEquality.swObjectSame ||
                    application.IsSame(result.Segment.GetSketch(), sketch) != (int)swObjectEquality.swObjectSame)
                    throw new InvalidOperationException("原路径草图不再包含唯一的本次圆弧。");
                guard();
                VerifyArc(result.Segment, g, guard);

                stage = "检查实体几何和体积";
                var part = document as PartDoc ?? throw new InvalidOperationException("无法读取弯头零件实体。");
                VerifyBody(application, document, part, g, guard);
                guard();
                SolidWorksFileLogger.Info("保存后的弯头特征、原路径圆弧、环面、端部和体积再次校验通过。");
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidComObjectException ||
                ex is InvalidOperationException || ex is ArgumentException || ex is ArithmeticException ||
                ex is NotSupportedException || ex is IOException || ex is UnauthorizedAccessException ||
                ex is System.Security.SecurityException)
            {
                SolidWorksFileLogger.Error("已保存弯头的后续复验失败；阶段=" + stage + "；异常类型=" +
                    ex.GetType().Name + "；未执行删除、关闭或回滚。");
                throw new PipelineElbowModelRevalidationException(stage, observedPath, ex);
            }
        }

        private static void RequireSavedTarget(SldWorks application, ModelDoc2 document, string path)
        {
            Straight.RequireActive(application, document);
            if (document.GetType() != (int)swDocumentTypes_e.swDocPART ||
                !string.Equals(document.GetPathName(), path, StringComparison.OrdinalIgnoreCase) ||
                document.GetSaveFlag())
                throw new InvalidOperationException("活动零件的保存路径或保存状态已改变。");
            Straight.RequireActive(application, document);
            if (!string.Equals(document.GetPathName(), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("活动零件的保存路径已改变。");
        }

        private static void RequireTarget(SldWorks application, ModelDoc2 document)
        {
            Straight.RequireActive(application, document);
            if (!string.IsNullOrEmpty(document.GetPathName()))
                throw new InvalidOperationException("本次零件已被保存，停止后续操作。");
        }

        private static void VerifyArc(SketchSegment segment, Geometry g, Action guard)
        {
            guard();
            var arc = segment as SketchArc ?? throw new InvalidOperationException("路径不是圆弧。");
            var start = arc.GetStartPoint2() as SketchPoint;
            var end = arc.GetEndPoint2() as SketchPoint;
            var center = arc.GetCenterPoint2() as SketchPoint;
            if (start == null || end == null || center == null ||
                !Geometry.Near(new Vector(start.X, start.Y, start.Z), g.Start, g.PositionTolerance) ||
                !Geometry.Near(new Vector(end.X, end.Y, end.Z), g.End, g.PositionTolerance) ||
                !Geometry.Near(new Vector(center.X, center.Y, center.Z), g.Center, g.PositionTolerance) ||
                !Geometry.Near(arc.GetRadius(), g.Radius, g.PositionTolerance) ||
                !Geometry.Near(segment.GetLength(), g.ArcLength, g.PositionTolerance * 2))
                throw new InvalidOperationException("圆弧端点、圆心、半径或弧长不一致。");
            guard();
        }

        private static void VerifyBody(SldWorks app, ModelDoc2 doc, PartDoc part, Geometry g, Action guard)
        {
            guard();
            var bodies = Straight.ReadComObjects(part.GetBodies2((int)swBodyType_e.swSolidBody, false), "实体");
            guard();
            var sheets = Straight.ReadComObjects(part.GetBodies2((int)swBodyType_e.swSheetBody, false), "曲面");
            guard();
            var visible = Straight.ReadComObjects(part.GetBodies2((int)swBodyType_e.swSolidBody, true), "可见实体");
            if (bodies.Count != 1 || sheets.Count != 0 || visible.Count != 1 ||
                app.IsSame(bodies[0], visible[0]) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("零件不是恰好一个可见实体，或包含曲面。");
            var body = bodies[0] as Body2 ?? throw new InvalidOperationException("无法读取实体。");
            if (body.GetType() != (int)swBodyType_e.swSolidBody) throw new InvalidOperationException("实体类型无效。");
            var faces = Straight.ReadComObjects(body.GetFaces(), "实体面");
            if (faces.Count != 4) throw new InvalidOperationException("实体面数量不符合预期。");
            var planes = new List<Face2>();
            Face2? inner = null, outer = null;
            foreach (object item in faces)
            {
                guard();
                var face = item as Face2 ?? throw new InvalidOperationException("无法读取实体面。");
                var surface = face.GetSurface() as Surface ?? throw new InvalidOperationException("无法读取曲面。");
                if (surface.IsPlane()) { planes.Add(face); continue; }
                if (!surface.IsTorus()) throw new InvalidOperationException("实体包含非预期曲面。");
                var p = surface.TorusParams as double[];
                if (p == null || p.Length < 8) throw new InvalidOperationException("无法读取环面参数。");
                Vector center = Geometry.FromArray(p, 0), axis = Geometry.FromArray(p, 3).Unit();
                if (!Geometry.Near(center, g.Center, g.PositionTolerance) ||
                    axis.Cross(g.Axis).Length() > 1e-8 ||
                    !Geometry.Near(p[6], g.Radius, g.PositionTolerance))
                    throw new InvalidOperationException("环面圆心、轴线或主半径不一致。");
                if (Geometry.Near(p[7], g.Inner, g.RadiusTolerance))
                {
                    if (inner != null) throw new InvalidOperationException("内环面不唯一。");
                    inner = face;
                }
                else if (Geometry.Near(p[7], g.Outer, g.RadiusTolerance))
                {
                    if (outer != null) throw new InvalidOperationException("外环面不唯一。");
                    outer = face;
                }
                else throw new InvalidOperationException("环面管半径不一致。");
            }
            if (inner == null || outer == null || planes.Count != 2)
                throw new InvalidOperationException("未能验证内外环面及两个端面。");
            // 同一内环面须以完整内圆边连接两个不同端面；同时检查外圆边。
            int innerStart = VerifyBoundaries(app, inner, planes, g, g.Inner, guard);
            int outerStart = VerifyBoundaries(app, outer, planes, g, g.Outer, guard);
            if (innerStart != outerStart) throw new InvalidOperationException("内外圆边未连接同一对端面。");
            guard();
            var mass = doc.Extension.CreateMassProperty() as MassProperty ??
                throw new InvalidOperationException("无法计算实体体积。");
            if (!Geometry.Near(mass.Volume, g.Volume, g.Volume * 1e-5))
                throw new InvalidOperationException("实体体积与理论空心弯头不一致。");
            guard();
        }

        private static int VerifyBoundaries(SldWorks app, Face2 torus,
            List<Face2> planes, Geometry g, double radius, Action guard)
        {
            guard();
            var edges = Straight.ReadComObjects(torus.GetEdges(), "环面边界");
            int startPlane = -1, endPlane = -1, circles = 0;
            foreach (object item in edges)
            {
                guard();
                var edge = item as Edge ?? throw new InvalidOperationException("无法读取环面边界。");
                var curve = edge.GetCurve() as Curve ?? throw new InvalidOperationException("无法读取边界曲线。");
                if (!curve.IsCircle()) continue;
                // 环面接缝也可能是圆弧；只有与端面相接的圆边才是管口。
                int plane = -1;
                for (int i = 0; i < planes.Count; i++)
                {
                    guard();
                    foreach (object candidate in Straight.ReadComObjects(planes[i].GetEdges(), "端面边界"))
                    {
                        guard();
                        int same = app.IsSame(candidate, edge);
                        if (same == (int)swObjectEquality.swObjectSame)
                        {
                            if (plane != -1) throw new InvalidOperationException("圆边连接多个端面。");
                            plane = i;
                        }
                        else if (same != (int)swObjectEquality.swObjectNotSame)
                            throw new InvalidOperationException("无法比较边界身份。");
                    }
                }
                if (plane < 0) continue;
                circles++;
                var p = curve.CircleParams as double[];
                if (p == null || p.Length < 7) throw new InvalidOperationException("无法读取端部圆边参数。");
                Vector center = Geometry.FromArray(p, 0), normal = Geometry.FromArray(p, 3).Unit();
                bool start = Geometry.Near(center, g.Start, g.PositionTolerance);
                bool end = Geometry.Near(center, g.End, g.PositionTolerance);
                if (start == end || !Geometry.Near(p[6], radius, g.RadiusTolerance) ||
                    normal.Cross(start ? g.StartTangent : g.EndTangent).Length() * radius > g.PositionTolerance)
                    throw new InvalidOperationException("端部圆边的圆心、法向或半径不一致。");
                CurveParamData data = edge.GetCurveParams3();
                if (data == null || double.IsNaN(data.UMinValue) || double.IsInfinity(data.UMinValue) ||
                    double.IsNaN(data.UMaxValue) || double.IsInfinity(data.UMaxValue) || data.UMaxValue <= data.UMinValue)
                    throw new InvalidOperationException("圆边裁剪参数无效。");
                double circumference = 2 * Math.PI * radius;
                double tolerance = Math.Max(circumference * 1e-8, g.PositionTolerance * 2 * Math.PI);
                if (tolerance >= circumference / 2 ||
                    !Geometry.Near(curve.GetLength3(data.UMinValue, data.UMaxValue), circumference, tolerance))
                    throw new InvalidOperationException("端部圆边不是完整圆。");
                Vector first = Geometry.FromArray(data.StartPoint as double[], 0);
                Vector last = Geometry.FromArray(data.EndPoint as double[], 0);
                Vector offset = first - center;
                if (!Geometry.Near(first, last, g.PositionTolerance) ||
                    !Geometry.Near(offset.Length(), radius, g.PositionTolerance) ||
                    Math.Abs(offset.Dot(normal)) > g.PositionTolerance)
                    throw new InvalidOperationException("圆边实际裁剪端点无效。");
                if ((start && startPlane >= 0) || (end && endPlane >= 0))
                    throw new InvalidOperationException("端部圆边连接关系无效。");
                if (start) startPlane = plane; else endPlane = plane;
            }
            if (circles != 2 || startPlane < 0 || endPlane < 0 || startPlane == endPlane)
                throw new InvalidOperationException("无法确认同一环面连接两个完整端部圆边。");
            guard();
            return startPlane;
        }
    }
}

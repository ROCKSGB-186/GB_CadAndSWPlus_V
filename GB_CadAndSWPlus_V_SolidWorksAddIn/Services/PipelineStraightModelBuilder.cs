using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>新建的未保存零件及其直管特征；调用方负责后续处理。</summary>
    internal sealed class PipelineStraightModelResult
    {
        internal PipelineStraightModelResult(ModelDoc2 document, Feature feature, string title)
        {
            Document = document;
            Feature = feature;
            Title = title;
        }

        public ModelDoc2 Document { get; }
        public Feature Feature { get; }
        public string Title { get; }
    }

    /// <summary>失败不代表已回滚；零件可能仍然打开或部分建成。</summary>
    internal sealed class PipelineStraightModelBuildException : Exception
    {
        internal PipelineStraightModelBuildException(string message, string? documentTitle, Exception innerException)
            : base(message, innerException)
        {
            DocumentTitle = documentTitle;
        }

        public string? DocumentTitle { get; }
    }

    /// <summary>
    /// 仅在新建、未保存的独立零件中生成两自由端直管；不保存、插入或关闭文档。
    /// 坐标、外径和壁厚均为毫米，草图及 SolidWorks 几何校验使用米。
    /// </summary>
    internal static class PipelineStraightModelBuilder
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        /// <summary>
        /// 必须从 SolidWorks 主窗口所属的进程内 UI 线程调用，并传入该窗口句柄。
        /// 模板必须是用户指定的本地或可访问的 .prtdot 文件；成功后零件保持打开且未保存。
        /// 不支持连接端、多段路线、装配、保存或 CAD 同步。
        /// </summary>
        public static PipelineStraightModelResult Build(
            SldWorks application, IntPtr mainWindowHandle, string partTemplatePath,
            PipeRoute route, double outsideDiameterMm, double wallThicknessMm)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            RequireUiThread(mainWindowHandle);
            ValidateInput(partTemplatePath, route, outsideDiameterMm, wallThicknessMm);

            string? title = null;
            ModelDoc2? document = null;
            bool newDocumentAttempted = false;
            string stage = "确认 SolidWorks 主窗口";
            try
            {
                var frame = application.Frame() as Frame;
                if (frame == null || new IntPtr(frame.GetHWndx64()) != mainWindowHandle)
                    throw new InvalidOperationException("传入的窗口不是该 SolidWorks 实例的主窗口。");

                // 保存创建前的文档身份，防止错误地在已有文档上建模。
                stage = "记录已有文档";
                List<object> existingDocuments = ReadComObjects(application.GetDocuments(), "已有文档列表");
                stage = "新建未保存零件";
                SolidWorksFileLogger.Info("开始新建独立未保存直管零件。");
                newDocumentAttempted = true;
                document = application.NewDocument(partTemplatePath, 0, 0, 0) as ModelDoc2;
                if (document == null)
                    throw new InvalidOperationException("新建文档未返回零件对象，新文档可能残留。");

                RequireActive(application, document);
                foreach (object existing in existingDocuments)
                {
                    if (application.IsSame(existing, document) != (int)swObjectEquality.swObjectNotSame)
                        throw new InvalidOperationException("无法确认返回的文档是本次新建的独立零件。");
                }

                title = document.GetTitle();
                if (string.IsNullOrWhiteSpace(title))
                    throw new InvalidOperationException("新零件标题不可用。");
                if (document.GetType() != (int)swDocumentTypes_e.swDocPART ||
                    !string.IsNullOrEmpty(document.GetPathName()))
                    throw new InvalidOperationException("新建文档不是未保存的零件。");
                var part = document as PartDoc;
                var sketchManager = document.SketchManager;
                var featureManager = document.FeatureManager;
                if (part == null || sketchManager == null || featureManager == null)
                    throw new InvalidOperationException("新零件接口不可用。");
                if (sketchManager.ActiveSketch != null)
                    throw new InvalidOperationException("新零件已有活动草图，不能继续。");
                if (ReadComObjects(part.GetBodies2((int)swBodyType_e.swSolidBody, false), "初始实体").Count != 0 ||
                    ReadComObjects(part.GetBodies2((int)swBodyType_e.swSheetBody, false), "初始曲面").Count != 0)
                    throw new InvalidOperationException("零件模板包含已有实体或曲面，不能用于本操作。");

                stage = "创建三维路径";
                RequireActive(application, document);
                List<object> oldFeatures = ReadFeatures(document);
                RequireActive(application, document);
                document.ClearSelection2(true);
                RequireActive(application, document);
                sketchManager.Insert3DSketch(true);
                RequireActive(application, document);
                if (sketchManager.ActiveSketch == null)
                    throw new InvalidOperationException("未能进入新零件的三维草图。");

                PipePoint3D start = route.Points[0];
                PipePoint3D end = route.Points[1];
                RequireActive(application, document);
                SketchSegment segment = sketchManager.CreateLine(
                    start.X / 1000.0, start.Y / 1000.0, start.Z / 1000.0,
                    end.X / 1000.0, end.Y / 1000.0, end.Z / 1000.0);
                if (segment == null) throw new InvalidOperationException("三维路径直线创建失败。");
                RequireActive(application, document);
                VerifyLine(segment, start, end);

                RequireActive(application, document);
                sketchManager.Insert3DSketch(true);
                RequireActive(application, document);
                if (sketchManager.ActiveSketch != null)
                    throw new InvalidOperationException("未能退出三维草图。");
                Feature path = FindNewPath(application, document, oldFeatures, segment);
                RequireActive(application, document);
                VerifyLine(segment, start, end);
                SolidWorksFileLogger.Info("新零件三维直线路径已创建并校验端点。");

                stage = "创建薄壁圆形扫掠";
                RequireActive(application, document);
                document.ClearSelection2(true);
                RequireActive(application, document);
                if (!path.Select2(false, 4))
                    throw new InvalidOperationException("无法以路径标记选择三维草图。");
                RequireActive(application, document);
                var sweep = featureManager.CreateDefinition((int)swFeatureNameID_e.swFmSweep) as SweepFeatureData;
                if (sweep == null) throw new InvalidOperationException("无法创建扫掠参数对象。");

                // 已验证的薄壁单方向扫掠：圆形轮廓为内径，壁厚向外增加。
                sweep.CircularProfile = true;
                sweep.CircularProfileDiameter = ((outsideDiameterMm - wallThicknessMm) - wallThicknessMm) / 1000.0;
                sweep.ThinFeature = true;
                sweep.ThinWallType = (short)swThinWallType_e.swThinWallOneDirection;
                sweep.SetWallThickness(true, wallThicknessMm / 1000.0);
                RequireActive(application, document);
                Feature feature = featureManager.CreateFeature(sweep);
                if (feature == null) throw new InvalidOperationException("薄壁扫掠特征创建失败。");

                stage = "重建和校验直管";
                RequireActive(application, document);
                if (!document.EditRebuild3())
                    throw new InvalidOperationException("新零件重建失败。");
                RequireActive(application, document);
                bool warning;
                int featureError = feature.GetErrorCode2(out warning);
                if (featureError != 0 || warning)
                    throw new InvalidOperationException("扫掠特征存在错误或警告；错误码=" + featureError + "。");
                RequireActive(application, document);
                VerifyLine(segment, start, end);
                RequireActive(application, document);
                VerifyBody(application, document, part, start, end, outsideDiameterMm, wallThicknessMm);
                RequireActive(application, document);
                if (!string.IsNullOrEmpty(document.GetPathName()))
                    throw new InvalidOperationException("新零件已被外部操作保存，停止报告建模成功。");

                SolidWorksFileLogger.Info("独立直管零件重建完成，实体内外径及轴向起终点、长度校验通过；零件未保存。");
                return new PipelineStraightModelResult(document, feature, title);
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Error("独立直管零件生成失败；阶段=" + stage +
                    "；异常类型=" + ex.GetType().Name + "；未执行回滚或关闭。");
                string context = !newDocumentAttempted
                    ? "尚未尝试新建零件。"
                    : title == null
                        ? "新建结果或标题未知，新文档可能残留；未自动关闭或回滚。"
                        : "测试零件标题：“" + title + "”；该文档可能不完整，未自动关闭或回滚。";
                throw new PipelineStraightModelBuildException(
                    "直管生成失败；阶段=" + stage + "；原因=" + ex.Message + "。" + context, title, ex);
            }
        }

        private static void RequireUiThread(IntPtr window)
        {
            uint processId;
            uint threadId = GetWindowThreadProcessId(window, out processId);
            if (window == IntPtr.Zero || threadId == 0 || threadId != GetCurrentThreadId() ||
                processId != GetCurrentProcessId())
                throw new InvalidOperationException("必须在 SolidWorks 主窗口所属进程的 UI 线程调用。");
        }

        private static void ValidateInput(string template, PipeRoute route, double diameter, double wall)
        {
            if (string.IsNullOrWhiteSpace(template) || !IsFullyQualifiedPath(template) ||
                !string.Equals(Path.GetExtension(template), ".prtdot", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(template))
                throw new ArgumentException("必须指定存在的零件模板 .prtdot 绝对路径。", nameof(template));
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (route.Points.Count != 2 || route.StartKind != PipeEndpointKind.Free || route.EndKind != PipeEndpointKind.Free)
                throw new NotSupportedException("仅支持两个点、两个自由端的直管。");
            PipePoint3D.RequireFinite(diameter, nameof(diameter));
            PipePoint3D.RequireFinite(wall, nameof(wall));
            if (diameter <= 0 || wall <= 0 || wall >= diameter / 2.0)
                throw new ArgumentOutOfRangeException(nameof(diameter), "外径、壁厚和内径必须均大于零。");
            double innerDiameter = (diameter - wall) - wall;
            if (innerDiameter <= 0)
                throw new ArgumentOutOfRangeException(nameof(diameter), "内径必须大于零。");
            double outerRadiusM = ToMeters(diameter) / 2.0;
            double innerRadiusM = ToMeters(innerDiameter) / 2.0;
            ToMeters(wall);
            if (innerRadiusM <= 0 || outerRadiusM <= innerRadiusM)
                throw new ArithmeticException("米单位下的内外半径无法用浮点数区分。");

            PipePoint3D p = route.Points[0];
            PipePoint3D q = route.Points[1];
            foreach (double value in new[] { p.X, p.Y, p.Z, q.X, q.Y, q.Z })
            {
                PipePoint3D.RequireFinite(value, nameof(route));
                ToMeters(value);
            }
            double dx, dy, dz, length;
            GetPathDirection(p, q, out dx, out dy, out dz, out length);
        }

        private static bool IsFullyQualifiedPath(string path)
        {
            string root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) &&
                ((root.Length >= 3 && root[1] == ':' &&
                  (root[2] == '\\' || root[2] == '/')) ||
                 root.StartsWith(@"\\", StringComparison.Ordinal));
        }

        private static void RequireActive(SldWorks application, ModelDoc2 document)
        {
            object active = application.ActiveDoc;
            if (active == null || application.IsSame(active, document) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("本次新零件不再是活动文档，已停止后续操作。");
        }

        private static List<object> ReadComObjects(object value, string description)
        {
            var result = new List<object>();
            if (value == null) return result;
            var array = value as Array;
            if (array == null) throw new InvalidOperationException(description + "不是有效数组。");
            foreach (object item in array)
            {
                if (item == null) throw new InvalidOperationException(description + "包含空对象。");
                result.Add(item);
            }
            return result;
        }

        private static List<object> ReadFeatures(ModelDoc2 document)
        {
            var result = new List<object>();
            Feature? current = document.FirstFeature() as Feature;
            while (current != null)
            {
                if (result.Count >= 10000) throw new InvalidOperationException("特征树过长或存在循环。");
                result.Add(current);
                current = current.GetNextFeature() as Feature;
            }
            return result;
        }

        private static Feature FindNewPath(SldWorks application, ModelDoc2 document, List<object> oldFeatures, SketchSegment segment)
        {
            Feature? found = null;
            foreach (object item in ReadFeatures(document))
            {
                var feature = item as Feature;
                if (feature == null || feature.GetTypeName2() != "3DProfileFeature") continue;
                bool existed = false;
                foreach (object old in oldFeatures)
                {
                    int same = application.IsSame(old, feature);
                    if (same == (int)swObjectEquality.swObjectSame) { existed = true; break; }
                    if (same != (int)swObjectEquality.swObjectNotSame)
                        throw new InvalidOperationException("无法比较草图特征身份。");
                }
                if (existed) continue;
                var sketch = feature.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw new InvalidOperationException("新三维草图接口不可用。");
                List<object> segments = ReadComObjects(sketch.GetSketchSegments(), "三维草图线段");
                if (segments.Count != 1 || application.IsSame(segments[0], segment) != (int)swObjectEquality.swObjectSame)
                    throw new InvalidOperationException("新三维草图不包含唯一的目标路径。");
                if (found != null) throw new InvalidOperationException("发现多个新三维草图，无法安全确定路径。");
                found = feature;
            }
            return found ?? throw new InvalidOperationException("未找到本次创建的三维草图特征。");
        }

        private static void VerifyLine(SketchSegment segment, PipePoint3D start, PipePoint3D end)
        {
            double dx, dy, dz, length;
            GetPathDirection(start, end, out dx, out dy, out dz, out length);
            double tolerance = GetPathTolerance(start, end, length);
            var line = segment as SketchLine;
            var p = line?.GetStartPoint2() as SketchPoint;
            var q = line?.GetEndPoint2() as SketchPoint;
            if (p == null || q == null ||
                !Near(p.X, start.X / 1000.0, tolerance) ||
                !Near(p.Y, start.Y / 1000.0, tolerance) ||
                !Near(p.Z, start.Z / 1000.0, tolerance) ||
                !Near(q.X, end.X / 1000.0, tolerance) ||
                !Near(q.Y, end.Y / 1000.0, tolerance) ||
                !Near(q.Z, end.Z / 1000.0, tolerance))
                throw new InvalidOperationException("草图直线端点与请求坐标不一致。");
        }

        private static void VerifyBody(SldWorks application, ModelDoc2 document, PartDoc part, PipePoint3D start, PipePoint3D end, double diameterMm, double wallMm)
        {
            List<object> bodies = ReadComObjects(part.GetBodies2((int)swBodyType_e.swSolidBody, false), "零件实体");
            RequireActive(application, document);
            List<object> surfaces = ReadComObjects(part.GetBodies2((int)swBodyType_e.swSheetBody, false), "零件曲面");
            RequireActive(application, document);
            List<object> visibleBodies = ReadComObjects(part.GetBodies2((int)swBodyType_e.swSolidBody, true), "可见实体");
            if (bodies.Count != 1 || surfaces.Count != 0 || visibleBodies.Count != 1 ||
                application.IsSame(bodies[0], visibleBodies[0]) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("新零件不是恰好一个可见实体，或包含曲面。");
            var body = bodies[0] as Body2;
            if (body == null || body.GetType() != (int)swBodyType_e.swSolidBody)
                throw new InvalidOperationException("新零件不是实体管道。");
            List<object> faces = ReadComObjects(body.GetFaces(), "实体面");
            // 独立中空直管应只有内外圆柱面和两个端面；额外面可能表示孔内存在障碍物。
            if (faces.Count != 4)
                throw new InvalidOperationException("直管实体面数量不符合预期，无法确认内孔没有障碍物。");
            double outer = (diameterMm / 1000.0) / 2.0;
            double inner = (((diameterMm - wallMm) - wallMm) / 1000.0) / 2.0;
            // 内外半径的容差不能相互重叠，否则不能证明两个不同的圆柱面。
            double radiusTolerance = Math.Min(outer * 1e-6, (outer - inner) / 4.0);
            double dx, dy, dz, length;
            GetPathDirection(start, end, out dx, out dy, out dz, out length);
            double pathTolerance = GetPathTolerance(start, end, length);
            double axisTolerance = Math.Max(inner * 1e-6, CoordinatePrecision(start, end));
            if (axisTolerance >= inner / 2.0)
                throw new ArithmeticException("坐标精度不足，无法可靠校验管道轴线。");

            // 极值点位于端面边缘也可使用：投影后比较真实实体的轴向起点、终点及长度。
            RequireActive(application, document);
            double minX, minY, minZ, maxX, maxY, maxZ;
            if (!body.GetExtremePoint(-dx, -dy, -dz, out minX, out minY, out minZ))
                throw new InvalidOperationException("无法读取实体沿路径反方向的极值点。");
            RequireActive(application, document);
            if (!body.GetExtremePoint(dx, dy, dz, out maxX, out maxY, out maxZ))
                throw new InvalidOperationException("无法读取实体沿路径正方向的极值点。");
            double minimum = ProjectFromStart(minX, minY, minZ, start, dx, dy, dz);
            double maximum = ProjectFromStart(maxX, maxY, maxZ, start, dx, dy, dz);
            double actualLength = maximum - minimum;
            PipePoint3D.RequireFinite(actualLength, "实体轴向长度");
            if (actualLength <= 0 || !Near(minimum, 0, pathTolerance) ||
                !Near(maximum, length, pathTolerance) ||
                !Near(actualLength, length, pathTolerance * 2.0))
                throw new InvalidOperationException("实体的轴向起点、终点或长度与请求路径不一致。");
            bool hasInner = false, hasOuter = false, hasThroughBore = false;
            int planarFaces = 0;
            foreach (object item in faces)
            {
                RequireActive(application, document);
                var face = item as Face2;
                var surface = face?.GetSurface() as Surface;
                if (surface == null) throw new InvalidOperationException("无法读取实体面。");
                if (!surface.IsCylinder())
                {
                    if (!surface.IsPlane())
                        throw new InvalidOperationException("直管实体包含非预期的曲面。");
                    planarFaces++;
                    continue;
                }
                var values = surface.CylinderParams as double[];
                if (values == null || values.Length < 7)
                    throw new InvalidOperationException("无法读取圆柱面参数。");
                foreach (double value in values) PipePoint3D.RequireFinite(value, "圆柱面参数");
                double ax = values[3], ay = values[4], az = values[5];
                double axisLength = VectorLength(ax, ay, az);
                if (axisLength == 0) throw new InvalidOperationException("圆柱面轴向无效。");
                ax /= axisLength; ay /= axisLength; az /= axisLength;
                if (Math.Abs(ax * dx + ay * dy + az * dz) < 1.0 - 1e-8)
                    throw new InvalidOperationException("圆柱面方向与路径不一致。");
                double cx = values[0] - start.X / 1000.0;
                double cy = values[1] - start.Y / 1000.0;
                double cz = values[2] - start.Z / 1000.0;
                double along = cx * dx + cy * dy + cz * dz;
                PipePoint3D.RequireFinite(along, "圆柱面轴向投影");
                double offsetX = cx - along * dx, offsetY = cy - along * dy, offsetZ = cz - along * dz;
                if (VectorLength(offsetX, offsetY, offsetZ) > axisTolerance)
                    throw new InvalidOperationException("圆柱面轴线与路径不一致。");
                if (Near(values[6], inner, radiusTolerance))
                {
                    if (hasInner)
                        throw new InvalidOperationException("内圆柱面不唯一，无法确认内孔贯通。");
                    hasInner = true;
                    hasThroughBore = VerifyInnerFaceBoundaries(application, document, face, start, end,
                        dx, dy, dz, length, inner, radiusTolerance, axisTolerance, pathTolerance);
                }
                else if (Near(values[6], outer, radiusTolerance))
                {
                    if (hasOuter)
                        throw new InvalidOperationException("外圆柱面不唯一，无法确认直管形状。");
                    hasOuter = true;
                }
                else throw new InvalidOperationException("圆柱面半径与请求内外径不一致。");
            }
            if (!hasInner || !hasOuter || planarFaces != 2)
                throw new InvalidOperationException("未能验证真实内外圆柱面和两个端面。");
            if (!hasThroughBore)
                throw new InvalidOperationException("未能在同一内圆柱面上验证起点和终点的完整圆形边界，无法确认内孔贯通。");
        }

        private static bool VerifyInnerFaceBoundaries(SldWorks application, ModelDoc2 document, Face2 face,
            PipePoint3D start, PipePoint3D end, double dx, double dy, double dz, double length,
            double radius, double radiusTolerance, double axisTolerance, double pathTolerance)
        {
            bool hasStart = false, hasEnd = false;
            int circularEdges = 0;
            RequireActive(application, document);
            List<object> edges = ReadComObjects(face.GetEdges(), "内圆柱面边界");
            foreach (object item in edges)
            {
                RequireActive(application, document);
                var edge = item as Edge;
                if (edge == null) throw new InvalidOperationException("无法读取内圆柱面边界。");
                var curve = edge.GetCurve() as Curve;
                if (curve == null) throw new InvalidOperationException("无法读取内圆柱面边界曲线。");
                if (!curve.IsCircle()) continue;
                circularEdges++;
                var circle = curve.CircleParams as double[];
                if (circle == null || circle.Length < 7)
                    throw new InvalidOperationException("无法读取内孔圆边的圆心、轴向和半径。");
                foreach (double value in circle) PipePoint3D.RequireFinite(value, "内孔圆边参数");
                if (!Near(circle[6], radius, radiusTolerance))
                    throw new InvalidOperationException("内孔圆边半径与内径不一致。");

                double normalLength = VectorLength(circle[3], circle[4], circle[5]);
                if (normalLength == 0) throw new InvalidOperationException("内孔圆边法向无效。");
                double nx = circle[3] / normalLength, ny = circle[4] / normalLength, nz = circle[5] / normalLength;
                double crossX = ny * dz - nz * dy, crossY = nz * dx - nx * dz, crossZ = nx * dy - ny * dx;
                if (VectorLength(crossX, crossY, crossZ) * radius > Math.Min(pathTolerance, axisTolerance))
                    throw new InvalidOperationException("内孔圆边的平面不垂直于管道轴线。");

                double centerProjection = ProjectFromStart(circle[0], circle[1], circle[2], start, dx, dy, dz);
                double cx = circle[0] - ToMeters(start.X);
                double cy = circle[1] - ToMeters(start.Y);
                double cz = circle[2] - ToMeters(start.Z);
                if (VectorLength(cx - centerProjection * dx, cy - centerProjection * dy, cz - centerProjection * dz) > axisTolerance)
                    throw new InvalidOperationException("内孔圆边与管道轴线不同心。");
                bool atStart = Near(centerProjection, 0, pathTolerance);
                bool atEnd = Near(centerProjection, length, pathTolerance);
                if ((!atStart && !atEnd) || (atStart && hasStart) || (atEnd && hasEnd))
                    throw new InvalidOperationException("内圆柱面包含非端部或重复的圆形边界，无法确认内孔贯通。");

                // 底层圆本身可能是完整圆，必须检查边的实际裁剪参数和真实端点。
                CurveParamData parameters = edge.GetCurveParams3();
                if (parameters == null)
                    throw new InvalidOperationException("无法读取内孔圆边的裁剪参数。");
                double uMin = parameters.UMinValue, uMax = parameters.UMaxValue;
                PipePoint3D.RequireFinite(uMin, "圆边起始参数");
                PipePoint3D.RequireFinite(uMax, "圆边终止参数");
                if (uMax <= uMin)
                    throw new InvalidOperationException("内孔圆边裁剪参数无效，无法确认完整圆边。");
                double circumference = 2.0 * Math.PI * radius;
                PipePoint3D.RequireFinite(circumference, "内孔圆周长");
                RequireActive(application, document);
                double edgeLength = curve.GetLength3(uMin, uMax);
                double lengthTolerance = Math.Max(circumference * 1e-8, axisTolerance * 2.0 * Math.PI);
                if (lengthTolerance >= circumference / 2.0 ||
                    !Near(edgeLength, circumference, lengthTolerance))
                    throw new InvalidOperationException("内孔边界不是可验证的完整圆边。");
                double expectedProjection = atStart ? 0 : length;
                double[] edgeStart = VerifyCircleBoundaryPoint(parameters.StartPoint, circle, start, dx, dy, dz,
                    expectedProjection, radius, axisTolerance, pathTolerance);
                double[] edgeEnd = VerifyCircleBoundaryPoint(parameters.EndPoint, circle, start, dx, dy, dz,
                    expectedProjection, radius, axisTolerance, pathTolerance);
                if (VectorLength(edgeStart[0] - edgeEnd[0], edgeStart[1] - edgeEnd[1], edgeStart[2] - edgeEnd[2]) > axisTolerance)
                    throw new InvalidOperationException("内孔圆边的裁剪端点未闭合。");
                if (atStart) hasStart = true;
                if (atEnd) hasEnd = true;
            }
            // 同一个内圆柱面必须同时到达两个端面，避免两个分离的盲孔误判为贯通。
            return circularEdges == 2 && hasStart && hasEnd;
        }

        private static double[] VerifyCircleBoundaryPoint(object value, double[] circle, PipePoint3D start,
            double dx, double dy, double dz, double expectedProjection, double radius,
            double axisTolerance, double pathTolerance)
        {
            var point = value as double[];
            if (point == null || point.Length < 3)
                throw new InvalidOperationException("无法读取内孔圆边的实际端点。");
            double projection = ProjectFromStart(point[0], point[1], point[2], start, dx, dy, dz);
            if (!Near(projection, expectedProjection, pathTolerance) ||
                !Near(VectorLength(point[0] - circle[0], point[1] - circle[1], point[2] - circle[2]),
                    radius, axisTolerance))
                throw new InvalidOperationException("内孔圆边实际端点与端面位置或半径不一致。");
            return point;
        }

        private static double ToMeters(double millimeters)
        {
            double meters = millimeters / 1000.0;
            if (millimeters != 0 && meters == 0)
                throw new ArithmeticException("毫米转米时发生数值下溢。");
            PipePoint3D.RequireFinite(meters, "米单位数值");
            return meters;
        }

        private static void GetPathDirection(PipePoint3D start, PipePoint3D end,
            out double dx, out double dy, out double dz, out double length)
        {
            dx = ToMeters(end.X) - ToMeters(start.X);
            dy = ToMeters(end.Y) - ToMeters(start.Y);
            dz = ToMeters(end.Z) - ToMeters(start.Z);
            length = VectorLength(dx, dy, dz);
            if (length <= 0)
                throw new ArgumentException("路线必须具有在米单位下可表示的正长度。");
            dx /= length;
            dy /= length;
            dz /= length;
        }

        private static double VectorLength(double x, double y, double z)
        {
            PipePoint3D.RequireFinite(x, "向量分量");
            PipePoint3D.RequireFinite(y, "向量分量");
            PipePoint3D.RequireFinite(z, "向量分量");
            // 先按最大分量缩放，避免直接平方导致溢出或下溢。
            double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if (scale == 0) return 0;
            double sx = x / scale, sy = y / scale, sz = z / scale;
            double length = scale * Math.Sqrt(sx * sx + sy * sy + sz * sz);
            PipePoint3D.RequireFinite(length, "向量长度");
            return length;
        }

        private static double CoordinatePrecision(PipePoint3D start, PipePoint3D end)
        {
            double startScale = Math.Max(Math.Abs(ToMeters(start.X)),
                Math.Max(Math.Abs(ToMeters(start.Y)), Math.Abs(ToMeters(start.Z))));
            double endScale = Math.Max(Math.Abs(ToMeters(end.X)),
                Math.Max(Math.Abs(ToMeters(end.Y)), Math.Abs(ToMeters(end.Z))));
            double scale = Math.Max(startScale, endScale);
            // 预留 16 个 double 机器精度量级，用于坐标减法和投影。
            return scale * 3.552713678800501e-15;
        }

        private static double GetPathTolerance(PipePoint3D start, PipePoint3D end, double length)
        {
            double tolerance = Math.Max(length * 1e-8, CoordinatePrecision(start, end));
            if (tolerance >= length / 2.0)
                throw new ArithmeticException("坐标精度不足，无法可靠校验实体起终点和长度。");
            return tolerance;
        }

        private static double ProjectFromStart(double x, double y, double z, PipePoint3D start,
            double dx, double dy, double dz)
        {
            PipePoint3D.RequireFinite(x, "极值点坐标");
            PipePoint3D.RequireFinite(y, "极值点坐标");
            PipePoint3D.RequireFinite(z, "极值点坐标");
            double projection = (x - ToMeters(start.X)) * dx +
                (y - ToMeters(start.Y)) * dy + (z - ToMeters(start.Z)) * dz;
            PipePoint3D.RequireFinite(projection, "极值点投影");
            return projection;
        }

        private static bool Near(double actual, double expected, double tolerance)
        {
            return !double.IsNaN(actual) && !double.IsInfinity(actual) &&
                Math.Abs(actual - expected) <= tolerance;
        }
    }
}

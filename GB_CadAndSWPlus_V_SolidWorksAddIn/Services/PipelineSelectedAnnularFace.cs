using System;
using System.Collections.Generic;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>只读捕获零件文档的单个环形面；不创建连接、配合或实例身份。</summary>
    internal sealed class PipelineSelectedAnnularFace
    {
        private readonly Face2 _face;
        private readonly string _configuration;

        private PipelineSelectedAnnularFace(Face2 face, string configuration, PipelineAnnularFaceGeometry geometry)
        {
            _face = face;
            _configuration = configuration;
            Geometry = geometry;
        }

        public PipelineAnnularFaceGeometry Geometry { get; }

        public static PipelineSelectedAnnularFace Read(SldWorks application, ModelDoc2 document)
        {
            RequirePart(application, document);
            var configuration = document.ConfigurationManager?.ActiveConfiguration
                ?? throw new InvalidOperationException("无法读取当前零件配置。");
            string configurationName = configuration.Name;
            if (string.IsNullOrWhiteSpace(configurationName))
                throw new InvalidOperationException("当前零件配置名称无效。");
            var selection = document.SelectionManager as SelectionMgr
                ?? throw new InvalidOperationException("无法读取 SolidWorks 当前选择。");
            if (selection.GetSelectedObjectCount2(-1) != 1 ||
                selection.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelFACES)
                throw new InvalidOperationException("请在零件中仅选择一个环形平面，再读取端面。");
            var face = selection.GetSelectedObject6(1, -1) as Face2
                ?? throw new InvalidOperationException("无法读取所选端面。");
            var surface = face.GetSurface() as Surface;
            if (surface == null || !surface.IsPlane())
                throw new InvalidOperationException("所选面不是可读取的平面。");
            var normal = face.Normal as double[];
            if (normal == null || normal.Length < 3)
                throw new InvalidOperationException("无法读取端面法向。");
            var edges = face.GetEdges() as Array;
            if (edges == null || edges.Length != 2)
                throw new InvalidOperationException("当前试验仅支持两个圆边、没有其他边界的环形端面。");
            var boundaries = new List<PipelineCircularBoundary>();
            foreach (object item in edges)
            {
                var edge = item as Edge ?? throw new InvalidOperationException("无法读取端面边界。");
                var curve = edge.GetCurve() as Curve;
                if (curve == null || !curve.IsCircle())
                    throw new InvalidOperationException("端面边界不是圆形。");
                var parameters = curve.CircleParams as double[];
                var range = edge.GetCurveParams3();
                if (parameters == null || parameters.Length < 7 || range == null ||
                    !Finite(range.UMinValue) || !Finite(range.UMaxValue) || range.UMaxValue <= range.UMinValue)
                    throw new InvalidOperationException("无法读取圆边参数。");
                double length = curve.GetLength3(range.UMinValue, range.UMaxValue) * 1000;
                boundaries.Add(new PipelineCircularBoundary(
                    new PipePoint3D(parameters[0] * 1000, parameters[1] * 1000, parameters[2] * 1000),
                    new PipeDirection3D(parameters[3], parameters[4], parameters[5]),
                    parameters[6] * 1000, length));
            }
            var geometry = PipelineAnnularFaceGeometry.Create(
                new PipeDirection3D(normal[0], normal[1], normal[2]), boundaries);
            RequirePart(application, document);
            if (!string.Equals(document.ConfigurationManager?.ActiveConfiguration?.Name, configurationName, StringComparison.Ordinal) ||
                selection.GetSelectedObjectCount2(-1) != 1 ||
                application.IsSame(selection.GetSelectedObject6(1, -1), face) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("读取期间配置或选择发生变化，请重新选择。");
            SolidWorksFileLogger.Info("已只读检查零件环形端面；坐标单位=毫米；未建立管口关联。");
            return new PipelineSelectedAnnularFace(face, configurationName, geometry);
        }

        public void Revalidate(SldWorks application, ModelDoc2 document)
        {
            PipelineSelectedAnnularFace current = Read(application, document);
            if (application.IsSame(_face, current._face) != (int)swObjectEquality.swObjectSame ||
                !string.Equals(_configuration, current._configuration, StringComparison.Ordinal) ||
                !Near(Geometry.Center.X, current.Geometry.Center.X) ||
                !Near(Geometry.Center.Y, current.Geometry.Center.Y) ||
                !Near(Geometry.Center.Z, current.Geometry.Center.Z) ||
                !Near(Geometry.Normal.X, current.Geometry.Normal.X) ||
                !Near(Geometry.Normal.Y, current.Geometry.Normal.Y) ||
                !Near(Geometry.Normal.Z, current.Geometry.Normal.Z) ||
                !Near(Geometry.InnerRadiusMm, current.Geometry.InnerRadiusMm) ||
                !Near(Geometry.OuterRadiusMm, current.Geometry.OuterRadiusMm))
                throw new InvalidOperationException("确认期间端面、配置或几何已变化，请重新选择。");
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Near(double a, double b) => Finite(a) && Finite(b) && Math.Abs(a - b) <= 1e-8;

        private static void RequirePart(SldWorks application, ModelDoc2 document)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (document == null) throw new ArgumentNullException(nameof(document));
            var active = application.ActiveDoc as ModelDoc2;
            if (active == null || application.IsSame(active, document) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("活动文档已变化，请切回开始绘管时的零件。");
            if (document.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("当前端面取点试验仅支持零件文档；装配体坐标转换尚未验证。");
        }
    }
}

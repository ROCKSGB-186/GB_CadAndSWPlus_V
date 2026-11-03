using System;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    /// <summary>将正交视图位置投影到指定工作平面；调用方负责确认视口和鼠标坐标。</summary>
    internal static class PipelineViewPointProjection
    {
        public static PipeRayIntersectionResult IntersectWorkPlane(
            ModelView view, double viewX, double viewY, PipeWorkPlane plane, out PipePoint3D intersection)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            PipePoint3D.RequireFinite(viewX, nameof(viewX));
            PipePoint3D.RequireFinite(viewY, nameof(viewY));
            if (view.HasPerspective())
                throw new NotSupportedException("透视视图的三维坐标拾取尚未完成验证，请关闭透视后重试。");

            view.UnprojectModelPoint(viewX, viewY, 0, out double x0, out double y0, out double z0);
            view.UnprojectModelPoint(viewX, viewY, 1, out double x1, out double y1, out double z1);
            VerifyProjection(view, x0, y0, z0, viewX, viewY);
            VerifyProjection(view, x1, y1, z1, viewX, viewY);

            // SolidWorks 模型坐标使用米；统一路线模型使用毫米。
            var origin = new PipePoint3D(x0 * 1000, y0 * 1000, z0 * 1000);
            var direction = new PipeDirection3D((x1 - x0) * 1000, (y1 - y0) * 1000, (z1 - z0) * 1000);
            PipeRayIntersectionResult result = PipeRouteMath.IntersectRay(origin, direction, plane, out intersection);
            if (result == PipeRayIntersectionResult.BehindOrigin)
            {
                var reverse = new PipeDirection3D(-direction.X, -direction.Y, -direction.Z);
                result = PipeRouteMath.IntersectRay(origin, reverse, plane, out intersection);
            }
            return result;
        }

        private static void VerifyProjection(ModelView view, double x, double y, double z, double expectedX, double expectedY)
        {
            PipePoint3D.RequireFinite(x, nameof(x));
            PipePoint3D.RequireFinite(y, nameof(y));
            PipePoint3D.RequireFinite(z, nameof(z));
            view.ProjectModelPoint(x, y, z, out double projectedX, out double projectedY, out double projectedZ);
            PipePoint3D.RequireFinite(projectedX, nameof(projectedX));
            PipePoint3D.RequireFinite(projectedY, nameof(projectedY));
            PipePoint3D.RequireFinite(projectedZ, nameof(projectedZ));
            if (Math.Abs(projectedX - expectedX) > 0.001 || Math.Abs(projectedY - expectedY) > 0.001)
                throw new InvalidOperationException("视图坐标反投影校验失败，请检查当前视图后重试。");
        }
    }
}

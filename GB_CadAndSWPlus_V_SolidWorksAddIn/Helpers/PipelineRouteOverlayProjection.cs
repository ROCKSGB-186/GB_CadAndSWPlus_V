using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    /// <summary>将正交视图中的毫米路线投影为屏幕叠加线段。</summary>
    internal static class PipelineRouteOverlayProjection
    {
        private const uint GlViewport = 0x0BA2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetCurrentContext();

        [DllImport("opengl32.dll")]
        private static extern void glGetIntegerv(uint name, [Out] int[] values);

        /// <summary>只在当前视图绘制回调中调用；视口与视图客户区不一致时拒绝投影。</summary>
        public static IReadOnlyList<PipelineScreenLine> Project(ModelView view, IReadOnlyList<PipePoint3D> points)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (view.HasPerspective())
                throw new NotSupportedException("透视视图的路线预览尚未完成验证，请关闭透视后重试。");
            if (wglGetCurrentContext() == IntPtr.Zero)
                throw new InvalidOperationException("没有当前 OpenGL 图形上下文，无法读取路线预览视口。");

            var viewport = new int[4];
            glGetIntegerv(GlViewport, viewport);
            IntPtr window = new IntPtr(view.GetViewHWndx64());
            if (window == IntPtr.Zero)
                throw new InvalidOperationException("无法获取路线预览的视图窗口。");
            if (!GetClientRect(window, out NativeRect client))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取路线预览的视图尺寸。");
            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;

            // 仅使用已实测一致的坐标空间；分屏、DPI 等导致不一致时不能猜测偏移量。
            if (width <= 0 || height <= 0 || viewport[0] != 0 || viewport[1] != 0 ||
                viewport[2] != width || viewport[3] != height)
                throw new NotSupportedException("视图窗口与图形视口尺寸或原点不一致，无法安全显示路线预览。");

            var lines = new List<PipelineScreenLine>(Math.Max(0, points.Count - 1));
            if (points.Count < 2) return lines.AsReadOnly();
            ProjectPoint(view, points[0], width, height, out double previousX, out double previousY);
            for (int i = 1; i < points.Count; i++)
            {
                ProjectPoint(view, points[i], width, height, out double x, out double y);
                lines.Add(new PipelineScreenLine(previousX, previousY, x, y));
                previousX = x;
                previousY = y;
            }
            return lines.AsReadOnly();
        }

        private static void ProjectPoint(ModelView view, PipePoint3D point, int width, int height,
            out double x, out double y)
        {
            double modelX = ToMeters(point.X);
            double modelY = ToMeters(point.Y);
            double modelZ = ToMeters(point.Z);
            view.ProjectModelPoint(modelX, modelY, modelZ, out double viewX, out double viewY, out double viewZ);
            PipePoint3D.RequireFinite(viewX, "视图 X");
            PipePoint3D.RequireFinite(viewY, "视图 Y");
            PipePoint3D.RequireFinite(viewZ, "视图深度");
            x = 2.0 * (viewX / width) - 1.0;
            y = 1.0 - 2.0 * (viewY / height);
            PipePoint3D.RequireFinite(x, "归一化屏幕 X");
            PipePoint3D.RequireFinite(y, "归一化屏幕 Y");
        }

        private static double ToMeters(double millimeters)
        {
            double meters = millimeters / 1000.0;
            if (millimeters != 0 && meters == 0)
                throw new ArithmeticException("路线坐标由毫米转换为米时发生数值下溢。");
            return meters;
        }
    }
}

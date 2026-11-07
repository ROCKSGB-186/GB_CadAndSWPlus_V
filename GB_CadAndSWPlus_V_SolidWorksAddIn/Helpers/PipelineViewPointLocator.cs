using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    /// <summary>仅平移正交视图，将路线点显示在可见图形区中心；不修改模型或缩放。</summary>
    internal static class PipelineViewPointLocator
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X, Y; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);

        public static void Locate(SldWorks application, ModelDoc2 document, ModelView view,
            IntPtr window, PipePoint3D point)
        {
            CheckCurrent(application, document, view);
            if (view.HasPerspective() || view.Linked)
                throw new NotSupportedException("定位暂不支持透视或联动视图，请关闭透视或取消视图联动后重试。");
            if (!GetClientRect(window, out Rect client))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取视图客户区。");
            var origin = new Point();
            if (!ClientToScreen(window, ref origin))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取视图屏幕位置。");
            int[] visible = view.GetVisibleBox() as int[] ??
                throw new InvalidOperationException("无法读取视图可见范围。");
            if (visible.Length != 4 || client.Right <= client.Left || client.Bottom <= client.Top ||
                visible[0] < origin.X + client.Left || visible[1] < origin.Y + client.Top ||
                visible[2] > origin.X + client.Right || visible[3] > origin.Y + client.Bottom ||
                visible[2] <= visible[0] || visible[3] <= visible[1])
                throw new NotSupportedException("视图可见范围与客户区不一致，无法安全定位。");
            double centerX = ((double)visible[0] + visible[2]) / 2 - origin.X;
            double centerY = ((double)visible[1] + visible[3]) / 2 - origin.Y;

            Project(view, point.X / 1000, point.Y / 1000, point.Z / 1000, out double targetX, out double targetY);
            if (Math.Abs(targetX - centerX) <= 1 && Math.Abs(targetY - centerY) <= 1) return;

            // 先验证反投影，并以短距离标定实际视图平移，避免假设 DPI、缩放或 Y 轴符号。
            view.UnprojectModelPoint(centerX, centerY, 0, out double ax, out double ay, out double az);
            view.UnprojectModelPoint(centerX + 1, centerY, 0, out double bx, out double by, out double bz);
            view.UnprojectModelPoint(centerX, centerY + 1, 0, out double cx, out double cy, out double cz);
            Project(view, ax, ay, az, out double anchorX, out double anchorY);
            Project(view, bx, by, bz, out double checkX, out double checkY);
            if (Math.Abs(anchorX - centerX) > 0.001 || Math.Abs(anchorY - centerY) > 0.001 ||
                Math.Abs(checkX - centerX - 1) > 0.001 || Math.Abs(checkY - centerY) > 0.001)
                throw new InvalidOperationException("视图反投影校验失败，无法安全定位。");
            Project(view, cx, cy, cz, out checkX, out checkY);
            if (Math.Abs(checkX - centerX) > 0.001 || Math.Abs(checkY - centerY - 1) > 0.001)
                throw new InvalidOperationException("视图反投影校验失败，无法安全定位。");
            double stepX = Distance(ax, ay, az, bx, by, bz) * 16;
            double stepY = Distance(ax, ay, az, cx, cy, cz) * 16;
            if (!(stepX > 0) || !(stepY > 0)) throw new ArithmeticException("视图标定距离无效。");
            MathVector original = view.Translation3 ?? throw new InvalidOperationException("无法读取视图平移。");
            double[] data = original.ArrayData as double[] ?? throw new InvalidOperationException("无法读取视图平移数据。");
            MathUtility math = application.GetMathUtility() as MathUtility ??
                throw new InvalidOperationException("无法获取 SolidWorks 数学工具。");
            MathVector snapshot = math.CreateVector((double[])data.Clone()) as MathVector ??
                throw new InvalidOperationException("无法备份视图平移。");

            bool changed = false;
            try
            {
                CheckCurrent(application, document, view);
                changed = true;
                view.TranslateBy(stepX, 0);
                Project(view, ax, ay, az, out double x1, out double y1);
                view.TranslateBy(0, stepY);
                Project(view, ax, ay, az, out double x2, out double y2);
                double m11 = (x1 - anchorX) / stepX;
                double m21 = (y1 - anchorY) / stepX;
                double m12 = (x2 - x1) / stepY;
                double m22 = (y2 - y1) / stepY;
                if (Math.Abs(x1 - anchorX) + Math.Abs(y1 - anchorY) < 0.1 ||
                    Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 0.1 ||
                    Math.Abs(x1 - anchorX) + Math.Abs(y1 - anchorY) > 100 ||
                    Math.Abs(x2 - x1) + Math.Abs(y2 - y1) > 100)
                    throw new InvalidOperationException("视图平移标定超出安全范围。");
                Project(view, point.X / 1000, point.Y / 1000, point.Z / 1000, out targetX, out targetY);
                CalculateTranslation(m11, m12, m21, m22, centerX - targetX, centerY - targetY,
                    out double dx, out double dy);
                CheckCurrent(application, document, view);
                view.TranslateBy(dx, dy);
                Project(view, point.X / 1000, point.Y / 1000, point.Z / 1000, out targetX, out targetY);
                if (Math.Abs(targetX - centerX) > 1 || Math.Abs(targetY - centerY) > 1)
                    throw new InvalidOperationException("定位后坐标未到达视图中心。");
                document.GraphicsRedraw2();
            }
            catch (Exception ex) when (IsOperationException(ex))
            {
                if (!changed) throw;
                try
                {
                    CheckCurrent(application, document, view);
                    view.Translation3 = snapshot;
                    document.GraphicsRedraw2();
                    Project(view, ax, ay, az, out double restoredX, out double restoredY);
                    if (Math.Abs(restoredX - anchorX) > 0.01 || Math.Abs(restoredY - anchorY) > 0.01)
                        throw new InvalidOperationException("恢复后的视图位置校验失败。");
                }
                catch (Exception restoreError) when (IsOperationException(restoreError))
                {
                    throw new AggregateException("定位失败且视图未能确认恢复，请手动检查视图。", ex, restoreError);
                }
                throw new InvalidOperationException("定位失败，已恢复原视图位置：" + ex.Message, ex);
            }
        }

        internal static void CalculateTranslation(double m11, double m12, double m21, double m22,
            double x, double y, out double dx, out double dy)
        {
            double determinant = m11 * m22 - m12 * m21;
            PipePoint3D.RequireFinite(determinant, "视图平移标定行列式");
            if (determinant == 0) throw new InvalidOperationException("视图平移标定不可逆。");
            dx = (x * m22 - m12 * y) / determinant;
            dy = (m11 * y - x * m21) / determinant;
            PipePoint3D.RequireFinite(dx, "视图平移 X");
            PipePoint3D.RequireFinite(dy, "视图平移 Y");
        }

        private static double Distance(double ax, double ay, double az, double bx, double by, double bz)
        {
            double distance = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay) + (bz - az) * (bz - az));
            PipePoint3D.RequireFinite(distance, "视图标定距离");
            return distance;
        }

        private static void Project(ModelView view, double x, double y, double z, out double px, out double py)
        {
            PipePoint3D.RequireFinite(x, "模型 X");
            PipePoint3D.RequireFinite(y, "模型 Y");
            PipePoint3D.RequireFinite(z, "模型 Z");
            view.ProjectModelPoint(x, y, z, out px, out py, out double depth);
            PipePoint3D.RequireFinite(px, "视图 X");
            PipePoint3D.RequireFinite(py, "视图 Y");
            PipePoint3D.RequireFinite(depth, "视图深度");
        }

        private static void CheckCurrent(SldWorks application, ModelDoc2 document, ModelView view)
        {
            var current = application.ActiveDoc as ModelDoc2;
            if (current == null || application.IsSame(current, document) != (int)swObjectEquality.swObjectSame ||
                application.IsSame(document.ActiveView, view) != (int)swObjectEquality.swObjectSame ||
                view.HasPerspective() || view.Linked)
                throw new InvalidOperationException("活动文档或视图已切换，或视图已启用透视/联动。");
        }

        private static bool IsOperationException(Exception ex) =>
            ex is COMException || ex is InvalidComObjectException || ex is InvalidOperationException ||
            ex is ArgumentException || ex is ArithmeticException || ex is NotSupportedException ||
            ex is Win32Exception;
    }
}

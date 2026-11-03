using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    /// <summary>屏幕归一化坐标中的一条线段；坐标转换由视图层负责。</summary>
    internal struct PipelineScreenLine
    {
        public PipelineScreenLine(double x1, double y1, double x2, double y2)
        {
            if (!IsFinite(x1) || !IsFinite(y1) || !IsFinite(x2) || !IsFinite(y2))
                throw new ArgumentOutOfRangeException(nameof(x1), "屏幕坐标必须是有限数值。");
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// 在当前兼容 OpenGL 上下文中绘制屏幕叠加红线；只可从视图绘制回调使用。
    /// 不进行三维投影或深度遮挡；调用方必须处理返回的失败原因。
    /// </summary>
    internal static class PipelineScreenLineRenderer
    {
        private const uint GlVersion = 0x1F02;
        private const uint GlContextProfileMask = 0x9126;
        private const int GlCompatibilityProfileBit = 0x00000002;
        private const uint GlContextFlags = 0x821E;
        private const int GlForwardCompatibleBit = 0x00000001;
        private const uint GlExtensions = 0x1F03;
        private const uint GlCurrentProgram = 0x8B8D;
        private const uint GlProgramPipelineBinding = 0x825A;
        private const uint GlVertexProgramArb = 0x8620;
        private const uint GlFragmentProgramArb = 0x8804;
        private const uint GlAttribStackDepth = 0x0BB0;
        private const uint GlMaxAttribStackDepth = 0x0D35;
        private const uint GlModelviewStackDepth = 0x0BA3;
        private const uint GlMaxModelviewStackDepth = 0x0D36;
        private const uint GlProjectionStackDepth = 0x0BA4;
        private const uint GlMaxProjectionStackDepth = 0x0D38;
        private const uint GlStateMask = 0x00000001 | 0x00000004 | 0x00000100 | 0x00001000 | 0x00002000 | 0x00004000;
        private const uint GlProjection = 0x1701;
        private const uint GlModelview = 0x1700;
        private const uint GlLighting = 0x0B50;
        private const uint GlTexture2D = 0x0DE1;
        private const uint GlDepthTest = 0x0B71;
        private const uint GlBlend = 0x0BE2;
        private const uint GlScissorTest = 0x0C11;
        private const uint GlStencilTest = 0x0B90;
        private const uint GlAlphaTest = 0x0BC0;
        private const uint GlColorLogicOp = 0x0BF2;
        private const uint GlLines = 0x0001;

        [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentContext();
        [DllImport("opengl32.dll")] private static extern IntPtr wglGetCurrentDC();
        [DllImport("opengl32.dll")] private static extern IntPtr glGetString(uint name);
        [DllImport("opengl32.dll")] private static extern void glGetIntegerv(uint name, out int value);
        [DllImport("opengl32.dll")] private static extern void glPushAttrib(uint mask);
        [DllImport("opengl32.dll")] private static extern void glPopAttrib();
        [DllImport("opengl32.dll")] private static extern void glMatrixMode(uint mode);
        [DllImport("opengl32.dll")] private static extern void glPushMatrix();
        [DllImport("opengl32.dll")] private static extern void glPopMatrix();
        [DllImport("opengl32.dll")] private static extern void glLoadIdentity();
        [DllImport("opengl32.dll")] private static extern void glDisable(uint capability);
        [DllImport("opengl32.dll")] private static extern byte glIsEnabled(uint capability);
        [DllImport("opengl32.dll")] private static extern void glDepthMask(byte flag);
        [DllImport("opengl32.dll")] private static extern void glColorMask(byte red, byte green, byte blue, byte alpha);
        [DllImport("opengl32.dll")] private static extern void glLineWidth(float width);
        [DllImport("opengl32.dll")] private static extern void glColor3f(float red, float green, float blue);
        [DllImport("opengl32.dll")] private static extern void glBegin(uint mode);
        [DllImport("opengl32.dll")] private static extern void glVertex3d(double x, double y, double z);
        [DllImport("opengl32.dll")] private static extern void glEnd();

        public static bool TryDraw(IReadOnlyList<PipelineScreenLine> lines, out string error)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            var snapshot = new PipelineScreenLine[lines.Count];
            for (int i = 0; i < snapshot.Length; i++) snapshot[i] = lines[i];
            if (wglGetCurrentContext() == IntPtr.Zero || wglGetCurrentDC() == IntPtr.Zero)
            {
                error = "没有当前 OpenGL 上下文或设备上下文。";
                return false;
            }
            string versionText = Marshal.PtrToStringAnsi(glGetString(GlVersion)) ?? string.Empty;
            string versionPart = versionText.Split(' ')[0];
            if (!Version.TryParse(versionPart, out Version version) || version.Major < 2 ||
                (version.Major == 3 && version.Minor == 1))
            {
                error = "不支持或无法确认 OpenGL 版本：" + versionText;
                return false;
            }
            if (version.Major >= 3)
            {
                glGetIntegerv(GlContextFlags, out int flags);
                if ((flags & GlForwardCompatibleBit) != 0)
                {
                    error = "前向兼容 OpenGL 上下文不支持固定管线预览。";
                    return false;
                }
            }
            if (version.Major > 3 || (version.Major == 3 && version.Minor >= 2))
            {
                glGetIntegerv(GlContextProfileMask, out int profile);
                if ((profile & GlCompatibilityProfileBit) == 0)
                {
                    error = "当前不是 OpenGL 兼容配置，无法绘制路线预览。";
                    return false;
                }
            }

            string? extensions = Marshal.PtrToStringAnsi(glGetString(GlExtensions));
            if (extensions == null)
            {
                error = "无法检查 OpenGL 扩展，不能安全绘制路线预览。";
                return false;
            }
            string[] extensionNames = extensions.Split(' ');
            glGetIntegerv(GlCurrentProgram, out int program);
            bool supportsProgramPipeline = version.Major > 4 || (version.Major == 4 && version.Minor >= 1) ||
                Array.IndexOf(extensionNames, "GL_ARB_separate_shader_objects") >= 0;
            int pipeline = 0;
            if (supportsProgramPipeline) glGetIntegerv(GlProgramPipelineBinding, out pipeline);
            bool legacyProgramActive =
                (Array.IndexOf(extensionNames, "GL_ARB_vertex_program") >= 0 && glIsEnabled(GlVertexProgramArb) != 0) ||
                (Array.IndexOf(extensionNames, "GL_ARB_fragment_program") >= 0 && glIsEnabled(GlFragmentProgramArb) != 0);
            glGetIntegerv(GlAttribStackDepth, out int attribDepth);
            glGetIntegerv(GlMaxAttribStackDepth, out int attribMax);
            glGetIntegerv(GlModelviewStackDepth, out int modelDepth);
            glGetIntegerv(GlMaxModelviewStackDepth, out int modelMax);
            glGetIntegerv(GlProjectionStackDepth, out int projectionDepth);
            glGetIntegerv(GlMaxProjectionStackDepth, out int projectionMax);
            if (program != 0 || pipeline != 0 || legacyProgramActive ||
                attribDepth >= attribMax || modelDepth >= modelMax || projectionDepth >= projectionMax)
            {
                error = "当前着色器或状态栈不适合绘制路线预览。";
                return false;
            }

            // 保存并恢复图形状态与两个矩阵栈，不能改变 SolidWorks 自身的后续渲染。
            glPushAttrib(GlStateMask);
            try
            {
                glMatrixMode(GlProjection);
                glPushMatrix();
                try
                {
                    glLoadIdentity();
                    glMatrixMode(GlModelview);
                    glPushMatrix();
                    try
                    {
                        glLoadIdentity();
                        glDisable(GlLighting);
                        glDisable(GlTexture2D);
                        glDisable(GlDepthTest);
                        glDisable(GlBlend);
                        glDisable(GlScissorTest);
                        glDisable(GlStencilTest);
                        glDisable(GlAlphaTest);
                        glDisable(GlColorLogicOp);
                        glDepthMask(0);
                        glColorMask(1, 1, 1, 1);
                        glLineWidth(4);
                        glColor3f(1, 0, 0);
                        glBegin(GlLines);
                        try
                        {
                            foreach (PipelineScreenLine line in snapshot)
                            {
                                glVertex3d(line.X1, line.Y1, 0);
                                glVertex3d(line.X2, line.Y2, 0);
                            }
                        }
                        finally { glEnd(); }
                    }
                    finally
                    {
                        glMatrixMode(GlModelview);
                        glPopMatrix();
                    }
                }
                finally
                {
                    glMatrixMode(GlProjection);
                    glPopMatrix();
                }
            }
            finally
            {
                glPopAttrib();
            }
            error = string.Empty;
            return true;
        }
    }
}

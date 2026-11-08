using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>失败不代表回滚；调用方须根据插入尝试状态提示用户检查装配体。</summary>
    internal sealed class PipelineAssemblyTrialException : Exception
    {
        internal PipelineAssemblyTrialException(string stage, bool attempted, Exception inner)
            : base("装配体插入试运行失败；阶段=" + stage + "；原因=" + inner.Message +
                (attempted ? "。已尝试插入，装配体可能已有新增组件，请先检查；未自动删除或回滚。" :
                    "。尚未尝试插入组件。"), inner)
        {
            InsertionAttempted = attempted;
        }

        public bool InsertionAttempted { get; }
    }

    /// <summary>仅向受限的未保存测试装配体试插入，绝不自动保存或关闭任何文档。</summary>
    internal static class PipelineStraightAssemblyTrial
    {
        private const double MatrixTolerance = 1e-9;

        public static string ValidateTarget(SldWorks application, ModelDoc2 document,
            string configuration, IntPtr mainWindowHandle, IPipelineAssemblyComponentBaseline? baseline = null)
        {
            PipelineStraightModelBuilder.RequireUiThread(mainWindowHandle);
            RequireMainWindow(application, mainWindowHandle);
            var assembly = document as AssemblyDoc ??
                throw new InvalidOperationException("当前文档不是装配体。");
            RequireBeforeInsert(application, assembly, document, configuration, baseline);
            string title = document.GetTitle();
            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("无法确认试验装配体的标题。");
            return title;
        }

        public static PipelineSingleComponentBaseline CaptureSingleComponentTarget(
            SldWorks application, ModelDoc2 document, string configuration, IntPtr mainWindowHandle)
        {
            PipelineStraightModelBuilder.RequireUiThread(mainWindowHandle);
            RequireMainWindow(application, mainWindowHandle);
            var assembly = document as AssemblyDoc ??
                throw new InvalidOperationException("当前文档不是装配体。");
            RequireReady(application, assembly, document, configuration, false);
            RequireClearSelection(document);
            PipelineSingleComponentBaseline baseline = PipelineSingleComponentBaseline.Capture(application, assembly, document);
            baseline.Verify(application, assembly, document, null);
            SolidWorksFileLogger.Info("已记录单组件测试装配体基线，将在插入前后再次核对。");
            return baseline;
        }

        public static PipelineMultipleComponentBaseline CaptureMultipleComponentTarget(
            SldWorks application, ModelDoc2 document, string configuration, IntPtr mainWindowHandle)
        {
            PipelineStraightModelBuilder.RequireUiThread(mainWindowHandle);
            RequireMainWindow(application, mainWindowHandle);
            var assembly = document as AssemblyDoc ??
                throw new InvalidOperationException("当前文档不是装配体。");
            RequireReady(application, assembly, document, configuration, false);
            RequireClearSelection(document);
            var baseline = PipelineMultipleComponentBaseline.Capture(application, assembly, document);
            baseline.Verify(application, assembly, document, null);
            SolidWorksFileLogger.Info("已记录双零件测试装配体基线，将在插入前后再次核对。");
            return baseline;
        }

        public static void ActivateAndInsert(SldWorks application, ModelDoc2 assemblyDocument,
            PipelineStraightModelResult model, string path, string configuration, string originalTitle,
            IntPtr mainWindowHandle, bool autoRebuild, IPipelineAssemblyComponentBaseline? baseline = null)
        {
            bool attempted = false;
            string stage = "检查已保存的直管";
            try
            {
                PipelineStraightModelBuilder.RequireUiThread(mainWindowHandle);
                RequireMainWindow(application, mainWindowHandle);
                if (baseline != null && autoRebuild && !(baseline is PipelineMultipleComponentBaseline))
                    throw new InvalidOperationException("仅双零件测试装配体允许选择自动重建。");
                RequirePart(application, model.Document, path, true);
                var part = model.Document as PartDoc ??
                    throw new InvalidOperationException("无法读取已保存直管的零件接口。");
                string partConfiguration = ConfigurationName(model.Document);
                double[] center = BoundingBoxCenter(part);

                stage = "确认原试验装配体";
                if (!string.Equals(assemblyDocument.GetTitle(), originalTitle, StringComparison.Ordinal))
                    throw new InvalidOperationException("原装配体标题已变化，不能安全切换。");
                RequireUniqueOpenTitle(application, assemblyDocument, originalTitle);
                application.GetRunningCommandInfo(out int command, out string commandTitle, out bool commandUiActive);
                if (commandUiActive)
                    throw new InvalidOperationException("SolidWorks 当前有活动命令，请先结束。");
                int activationErrors = 0;
                var activated = application.ActivateDoc3(originalTitle, false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationErrors) as ModelDoc2;
                if (activationErrors != 0 || activated == null || !Same(application, activated, assemblyDocument))
                    throw new InvalidOperationException("未能安全切回原装配体；激活错误码=" + activationErrors + "。");
                var assembly = assemblyDocument as AssemblyDoc ??
                    throw new InvalidOperationException("原装配体接口不可用。");
                RequireBeforeInsert(application, assembly, assemblyDocument, configuration, baseline);
                RequirePart(application, model.Document, path, false);
                if (!string.Equals(ConfigurationName(model.Document), partConfiguration, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("直管零件配置已变化。");

                stage = "准备组件位置";
                var math = application.GetMathUtility() as MathUtility ??
                    throw new InvalidOperationException("无法获取 SolidWorks 数学工具。");
                var identity = math.CreateTransform(new double[] {
                    1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0
                }) as MathTransform ?? throw new InvalidOperationException("无法创建单位变换。");
                RequireIdentity(math, identity);
                RequireBeforeInsert(application, assembly, assemblyDocument, configuration, baseline);
                RequirePart(application, model.Document, path, false);

                stage = "插入组件";
                // AddComponent5 的位置针对包围盒中心；先放在其本地中心，再校验根坐标单位变换。
                attempted = true;
                SolidWorksFileLogger.Info(baseline == null
                    ? "开始向新建空装配体试插入直管；失败时组件可能残留。"
                    : "开始向已有组件的测试装配体试插入直管；失败时组件或变更可能残留，不自动回滚。");
                Component2 component = assembly.AddComponent5(path,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    string.Empty, false, string.Empty, center[0], center[1], center[2]);
                if (component == null)
                    throw new InvalidOperationException("插入接口未返回组件，不能确认是否已插入。");
                RequireReady(application, assembly, assemblyDocument, configuration, false);
                RequirePart(application, model.Document, path, false);
                if (baseline == null) RequireOnlyInsertedComponent(application, assembly, component);
                else baseline.Verify(application, assembly, assemblyDocument, component);
                if (baseline != null && component.IsFixed())
                    throw new InvalidOperationException("新直管不是预期的浮动组件，未自动更改固定状态。");
                if (!PathsEqual(component.GetPathName(), path) ||
                    !string.Equals(component.ReferencedConfiguration, partConfiguration, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("插入组件的路径或配置与已保存直管不一致。");
                object componentModel = component.GetModelDoc2();
                if (componentModel == null || !Same(application, componentModel, model.Document))
                    throw new InvalidOperationException("无法确认插入组件引用的是本次直管零件。");

                stage = "校验根坐标";
                var actual = component.Transform2;
                if (!IsIdentity(math, actual))
                {
                    // 只调整新增组件，不增加配合或更改原组件固定状态。
                    component.Transform2 = identity;
                    actual = component.Transform2;
                }
                RequireIdentity(math, actual);
                RequireInserted(application, assembly, assemblyDocument, model.Document, component,
                    math, path, configuration, partConfiguration, baseline);
                SolidWorksFileLogger.Info(baseline == null
                    ? "直管已插入新建空装配体，组件单位变换及引用校验通过；尚未保存装配体。"
                    : "直管已插入已有组件的测试装配体，原组件基线与新组件引用、位置校验通过；尚未重建或保存装配体。");
                if (autoRebuild)
                {
                    stage = "重建并校验试验装配体";
                    RebuildAndValidate(application, assembly, assemblyDocument, model, component,
                        math, path, configuration, partConfiguration, baseline);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException ||
                                       ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is System.Security.SecurityException ||
                                       ex is COMException || ex is InvalidComObjectException ||
                                       ex is Win32Exception || ex is NotSupportedException || ex is ArithmeticException)
            {
                SolidWorksFileLogger.Error("装配体直管插入试运行失败；阶段=" + stage + "；异常类型=" +
                    ex.GetType().Name + "；已尝试插入=" + (attempted ? "是" : "否") + "；未自动回滚。");
                throw new PipelineAssemblyTrialException(stage, attempted, ex);
            }
        }

        private static void RebuildAndValidate(SldWorks application, AssemblyDoc assembly, ModelDoc2 assemblyDocument,
            PipelineStraightModelResult model, Component2 component, MathUtility math, string path,
            string configuration, string partConfiguration, IPipelineAssemblyComponentBaseline? baseline)
        {
            RequireInserted(application, assembly, assemblyDocument, model.Document, component,
                math, path, configuration, partConfiguration, baseline);
            SolidWorksFileLogger.Info(baseline == null
                ? "用户选择自动重建，开始重建新建空装配体；不会自动保存。"
                : "用户选择自动重建，开始重建双零件测试装配体；不会自动保存，也不能保证回滚。");
            if (!assemblyDocument.EditRebuild3())
                throw new InvalidOperationException("SolidWorks 返回重建失败，不能确认装配体状态。");
            RequireInserted(application, assembly, assemblyDocument, model.Document, component,
                math, path, configuration, partConfiguration, baseline);
            bool warning;
            int featureError = model.Feature.GetErrorCode2(out warning);
            if (featureError != 0 || warning)
                throw new InvalidOperationException("重建后直管特征存在错误或警告；错误码=" + featureError + "。");
            SolidWorksFileLogger.Info("试验装配体重建完成，组件引用、位置及直管特征校验通过；未保存装配体。");
        }

        private static void RequireInserted(SldWorks application, AssemblyDoc assembly, ModelDoc2 assemblyDocument,
            ModelDoc2 partDocument, Component2 component, MathUtility math, string path,
            string configuration, string partConfiguration, IPipelineAssemblyComponentBaseline? baseline = null)
        {
            RequireReady(application, assembly, assemblyDocument, configuration, false);
            RequirePart(application, partDocument, path, false);
            if (baseline == null) RequireOnlyInsertedComponent(application, assembly, component);
            else
            {
                baseline.Verify(application, assembly, assemblyDocument, component);
                if (component.IsFixed())
                    throw new InvalidOperationException("新直管不再是浮动组件，未自动更改固定状态。");
            }
            RequireIdentity(math, component.Transform2);
            if (!PathsEqual(component.GetPathName(), path) ||
                !string.Equals(component.ReferencedConfiguration, partConfiguration, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ConfigurationName(partDocument), partConfiguration, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("组件引用的路径或配置已变化。");
            object componentModel = component.GetModelDoc2();
            if (componentModel == null || !Same(application, componentModel, partDocument))
                throw new InvalidOperationException("无法确认组件引用本次直管零件。");
        }

        private static void RequireBeforeInsert(SldWorks application, AssemblyDoc assembly, ModelDoc2 document,
            string configuration, IPipelineAssemblyComponentBaseline? baseline)
        {
            RequireReady(application, assembly, document, configuration, baseline == null);
            if (baseline != null)
            {
                RequireClearSelection(document);
                baseline.Verify(application, assembly, document, null);
            }
        }

        private static void RequireMainWindow(SldWorks application, IntPtr handle)
        {
            var frame = application.Frame() as Frame;
            if (frame == null || new IntPtr(frame.GetHWndx64()) != handle)
                throw new InvalidOperationException("当前窗口不是该 SolidWorks 实例的主窗口。");
        }

        private static void RequireReady(SldWorks application, AssemblyDoc assembly, ModelDoc2 document,
            string configuration, bool requireEmpty)
        {
            object active = application.ActiveDoc;
            if (active == null || !Same(application, active, document) ||
                document.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                throw new InvalidOperationException("原试验装配体不是活动文档。");
            if (document.IsOpenedReadOnly() || !string.IsNullOrEmpty(document.GetPathName()))
                throw new InvalidOperationException("试运行仅允许未保存且可编辑的测试装配体。");
            if (!string.Equals(PipelineAssemblyRootContext.Require(application, document, assembly),
                configuration, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("装配体配置已变化。");
            if (document.SketchManager == null || document.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("请先退出草图。");
            application.GetRunningCommandInfo(out int command, out string title, out bool uiActive);
            if (uiActive) throw new InvalidOperationException("SolidWorks 当前有活动命令或属性管理器。");
            if (requireEmpty)
            {
                if (assembly.GetComponentCount(false) != 0)
                    throw new InvalidOperationException("试运行仅允许没有组件的空装配体。");
                RequireClearSelection(document);
            }
        }

        private static void RequireClearSelection(ModelDoc2 document)
        {
            var selection = document.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) != 0)
                throw new InvalidOperationException("请先在测试装配体中清除当前选择，再执行插入试验。");
        }

        private static void RequireUniqueOpenTitle(SldWorks application, ModelDoc2 document, string title)
        {
            var documents = application.GetDocuments() as Array ??
                throw new InvalidOperationException("无法读取打开的 SolidWorks 文档。");
            int matches = 0;
            bool found = false;
            foreach (object item in documents)
            {
                var openDocument = item as ModelDoc2 ??
                    throw new InvalidOperationException("无法确认打开文档的身份。");
                if (string.Equals(openDocument.GetTitle(), title, StringComparison.OrdinalIgnoreCase))
                {
                    matches++;
                    if (Same(application, openDocument, document)) found = true;
                }
            }
            if (matches != 1 || !found)
                throw new InvalidOperationException("原装配体已关闭或文档标题不唯一，不能安全切换。");
        }

        private static void RequirePart(SldWorks application, ModelDoc2 document, string path, bool requireActive)
        {
            if (document.GetType() != (int)swDocumentTypes_e.swDocPART ||
                !PathsEqual(document.GetPathName(), path) || document.GetSaveFlag())
                throw new InvalidOperationException("直管零件的保存状态或路径已变化。");
            object? opened = application.GetOpenDocumentByName(path);
            if (opened == null || !Same(application, opened, document))
                throw new InvalidOperationException("无法确认已保存直管仍在当前 SolidWorks 实例中打开。");
            if (requireActive)
            {
                object active = application.ActiveDoc;
                if (active == null || !Same(application, active, document))
                    throw new InvalidOperationException("已保存直管不是当前活动文档。");
            }
        }

        internal static double[] BoundingBoxCenter(PartDoc part)
        {
            var box = part.GetPartBox(false) as double[];
            if (box == null || box.Length != 6)
                throw new InvalidOperationException("无法读取直管零件的包围盒。");
            var center = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (double.IsNaN(box[i]) || double.IsInfinity(box[i]) ||
                    double.IsNaN(box[i + 3]) || double.IsInfinity(box[i + 3]) || box[i] > box[i + 3])
                    throw new InvalidOperationException("直管包围盒无效。");
                center[i] = box[i] / 2.0 + box[i + 3] / 2.0;
                if (double.IsNaN(center[i]) || double.IsInfinity(center[i]))
                    throw new InvalidOperationException("直管包围盒中心无效。");
            }
            return center;
        }

        private static void RequireOnlyInsertedComponent(SldWorks application, AssemblyDoc assembly, Component2 component)
        {
            var components = assembly.GetComponents(true) as Array;
            if (assembly.GetComponentCount(false) != 1 || components == null || components.Length != 1)
                throw new InvalidOperationException("装配体组件数量不符合预期，请检查装配体。");
            foreach (object item in components)
                if (item != null && Same(application, item, component)) return;
            throw new InvalidOperationException("无法确认新增组件属于原装配体根级。");
        }

        private static string ConfigurationName(ModelDoc2 document)
        {
            string? name = document.ConfigurationManager?.ActiveConfiguration?.Name;
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("无法读取文档活动配置。");
            return name;
        }

        private static bool PathsEqual(string actual, string expected)
        {
            return !string.IsNullOrWhiteSpace(actual) &&
                string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Same(SldWorks application, object first, object second) =>
            application.IsSame(first, second) == (int)swObjectEquality.swObjectSame;

        internal static bool IsIdentity(MathUtility math, MathTransform transform)
        {
            var values = transform?.ArrayData as Array;
            if (values == null || values.Rank != 1 || values.Length != 16)
                throw new InvalidOperationException("组件变换矩阵无效。");
            int lower = values.GetLowerBound(0);
            for (int i = 0; i < 13; i++)
            {
                double expected = i == 0 || i == 4 || i == 8 || i == 12 ? 1 : 0;
                if (!(values.GetValue(lower + i) is double value) || double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidOperationException("组件变换矩阵数值无效。");
                if (Math.Abs(value - expected) > MatrixTolerance) return false;
            }
            foreach (double[] coordinates in new[] {
                new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 0.0, 0.0 },
                new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0 } })
            {
                var point = math.CreatePoint(coordinates) as MathPoint;
                var transformed = point?.MultiplyTransform(transform) as MathPoint;
                var result = transformed?.ArrayData as Array;
                if (result == null || result.Rank != 1 || result.Length != 3)
                    throw new InvalidOperationException("无法验证组件位置。");
                int first = result.GetLowerBound(0);
                for (int i = 0; i < 3; i++)
                {
                    if (!(result.GetValue(first + i) is double value) || double.IsNaN(value) || double.IsInfinity(value))
                        throw new InvalidOperationException("组件位置的点变换数值无效。");
                    if (Math.Abs(value - coordinates[i]) > MatrixTolerance) return false;
                }
            }
            return true;
        }

        internal static void RequireIdentity(MathUtility math, MathTransform transform)
        {
            if (!IsIdentity(math, transform))
                throw new InvalidOperationException("组件位置不是装配体根坐标单位变换。");
        }
    }
}

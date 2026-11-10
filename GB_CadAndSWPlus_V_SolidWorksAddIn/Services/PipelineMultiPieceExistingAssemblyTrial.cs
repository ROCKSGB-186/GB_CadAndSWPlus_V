using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class PipelineMultiPieceExistingAssemblyTrialException : Exception
    {
        internal PipelineMultiPieceExistingAssemblyTrialException(string stage, string? title,
            IList<string> paths, bool activationAttempted, int attempted, int returned, Exception inner)
            : base("已有零件装配体多段插入失败；阶段=" + stage + "；原因=" + inner.Message +
                "；装配体标题=" + (title ?? "未知") + "；本次保存文件=" + string.Join("、", paths) +
                "；激活已尝试=" + (activationAttempted ? "是" : "否") +
                "；插入尝试=" + attempted + "；已返回组件=" + returned +
                "。可能存在未知组件或其他变更残留；未自动保存、重建、配合、关闭、删除或回滚，请人工检查；不能防止外部并发变化。", inner)
        {
            Stage = stage;
            AssemblyTitle = title;
            SavedPaths = new ReadOnlyCollection<string>(new List<string>(paths));
            ActivationAttempted = activationAttempted;
            InsertionAttempted = attempted;
            InsertedCount = returned;
        }

        public string Stage { get; }
        public string? AssemblyTitle { get; }
        public IReadOnlyList<string> SavedPaths { get; }
        public bool ActivationAttempted { get; }
        public int InsertionAttempted { get; }
        public int InsertedCount { get; }
    }

    internal sealed class PipelineMultiPieceExistingAssemblyTrialResult
    {
        internal PipelineMultiPieceExistingAssemblyTrialResult(ModelDoc2 document, string title,
            string configuration, PipelineMultiPieceSavedResult saved, IList<Component2> components)
        {
            Document = document;
            AssemblyTitle = title;
            Configuration = configuration;
            Saved = saved;
            Components = new ReadOnlyCollection<Component2>(new List<Component2>(components));
        }

        public ModelDoc2 Document { get; }
        public string AssemblyTitle { get; }
        public string Configuration { get; }
        public PipelineMultiPieceSavedResult Saved { get; }
        public IReadOnlyList<Component2> Components { get; }
    }

    /// <summary>
    /// 仅在用户确认后，在 WithFreshSave 的同一 UI 同步回调中先保存再调用。
    /// 目标就是保存结果的源装配体；不提供事务、回滚或并发隔离。
    /// </summary>
    internal static class PipelineMultiPieceExistingAssemblyTrial
    {
        internal static PipelineMultiPieceExistingAssemblyTrialResult Build(SldWorks app, ModelDoc2 source,
            IntPtr window, string originalTitle, string configuration, PipelineMultipleComponentBaseline baseline,
            PipelineMultiPieceSavedResult saved)
        {
            string stage = "输入预检";
            bool activationAttempted = false;
            int attempted = 0;
            var inserted = new List<Component2>();
            var paths = new List<string>();
            var guards = new List<PipelineMultiPieceSaveProtection.FileGuard>();
            try
            {
                if (saved != null)
                    foreach (var item in saved.Parts) paths.Add(item == null ? "" : item.Path);
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (saved == null) throw new ArgumentNullException(nameof(saved));
                if (PipelineMultiPieceAssemblyTrial.CurrentScope == null)
                    throw new InvalidOperationException("必须在同一次 WithFreshSave 同步回调内保存并装配。");
                saved.Consume(app, source, window, requireScope: true);
                if (baseline == null) throw new ArgumentNullException(nameof(baseline));
                if (saved.Parts.Count == 0 || saved.Parts.Count != saved.Files.Count)
                    throw new InvalidOperationException("本次保存结果不完整。");
                if (string.IsNullOrWhiteSpace(originalTitle) || string.IsNullOrWhiteSpace(configuration) ||
                    !string.Equals(originalTitle, saved.SourceTitle, StringComparison.Ordinal) ||
                    !string.Equals(configuration, saved.SourceConfiguration, StringComparison.Ordinal) ||
                    saved.SourceType != (int)swDocumentTypes_e.swDocASSEMBLY ||
                    !string.IsNullOrEmpty(saved.SourcePath))
                    throw new InvalidOperationException("保存结果的源文档不是指定的未保存装配体或配置不符。");
                var assembly = source as AssemblyDoc ?? throw new InvalidOperationException("源文档没有装配体接口。");
                if (!Same(app, source, assembly)) throw new InvalidOperationException("无法确认装配体接口身份。");
                int count = saved.Parts.Count;
                var parts = new ModelDoc2[count];
                for (int i = 0; i < count; i++)
                {
                    var item = saved.Parts[i];
                    if (item == null || item.Document == null || string.IsNullOrWhiteSpace(item.Label))
                        throw new InvalidOperationException("本次保存零件记录不完整。");
                    parts[i] = item.Document;
                }

                stage = "保护并检查本次保存文件";
                for (int i = 0; i < count; i++)
                {
                    var guard = PipelineMultiPieceSaveProtection.FileGuard.Capture(paths[i]);
                    guards.Add(guard);
                    guard.CheckSnapshot(saved.Files[i]);
                }
                Action checkSaved = () => CheckSaved(app, source, window, saved, parts, guards, attempted != 0);
                checkSaved();
                // 保存完成时活动文档是最后一个零件，而不是源装配体。
                PipelineStraightModelBuilder.RequireActive(app, parts[count - 1]);
                RequireUniqueTitle(app, source, originalTitle);
                checkSaved();
                PipelineStraightModelBuilder.RequireActive(app, parts[count - 1]);
                RequireUniqueTitle(app, source, originalTitle);
                RequireContext(app, window);
                stage = "无重建激活原装配体";
                activationAttempted = true;
                SolidWorksFileLogger.Info("多段已有装配体试验：开始无重建激活原装配体；标题=" + originalTitle + "。");
                int errors = 0;
                var activated = app.ActivateDoc3(originalTitle, false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors) as ModelDoc2;
                if (errors != 0 || activated == null || !Same(app, activated, source))
                    throw new InvalidOperationException("未能安全激活原装配体；错误码=" + errors + "。");

                var math = app.GetMathUtility() as MathUtility ?? throw new InvalidOperationException("无法获取数学工具。");
                Action<bool> checkAll = allowNewest =>
                {
                    checkSaved();
                    RequireUniqueTitle(app, source, originalTitle);
                    RequireTarget(app, source, assembly, originalTitle, configuration);
                    baseline.VerifyAddedComponents(app, assembly, source, inserted);
                    for (int i = 0; i < inserted.Count; i++)
                    {
                        Component2 component = inserted[i];
                        var item = saved.Parts[i];
                        object model = component.GetModelDoc2();
                        if (component.GetSuppression2() != (int)swComponentSuppressionState_e.swComponentFullyResolved ||
                            component.IsGraphicsOnly || component.IsVirtual || component.HasUnloadedComponents() ||
                            component.GetHiddenUnloadedChildrenCount() != 0 || component.IsFixed() ||
                            !PathEquals(component.GetPathName(), item.Path) ||
                            !string.Equals(component.ReferencedConfiguration, item.Configuration, StringComparison.Ordinal) ||
                            model == null || !Same(app, model, parts[i]))
                            throw new InvalidOperationException("新增组件的解析状态、浮动状态、路径、配置或引用不符。");
                        if (!allowNewest || i != inserted.Count - 1)
                        {
                            MathTransform transform = component.Transform2;
                            PipelineStraightAssemblyTrial.RequireIdentity(math, transform);
                            if (!PipelineMultiPieceAssemblyTrial.IsExactIdentity(transform))
                                throw new InvalidOperationException("新增组件并非精确单位变换。");
                        }
                    }
                    checkSaved();
                    RequireUniqueTitle(app, source, originalTitle);
                    RequireTarget(app, source, assembly, originalTitle, configuration);
                    RequireContext(app, window);
                    PipelineStraightModelBuilder.RequireActive(app, source);
                };
                stage = "验证原装配体基线";
                checkAll(false);
                for (int i = 0; i < count; i++)
                {
                    string label = saved.Parts[i].Label;
                    stage = "插入前检查" + label;
                    checkAll(false);
                    var part = parts[i] as PartDoc ?? throw new InvalidOperationException("零件接口不可用。");
                    stage = "计算" + label + "米制实体包围盒";
                    double[] center = PipelineMultiPieceAssemblyTrial.SolidBodyCenterMeters(part);
                    checkAll(false);
                    stage = "插入" + label;
                    attempted++;
                    SolidWorksFileLogger.Info("多段已有装配体试验：开始插入" + label + "；路径=" + paths[i] + "；失败不回滚。");
                    Component2 component = assembly.AddComponent5(paths[i],
                        (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                        string.Empty, false, string.Empty, center[0], center[1], center[2]);
                    if (component == null) throw new InvalidOperationException("插入未返回组件，可能存在未知残留。");
                    inserted.Add(component);
                    stage = "调整前核验" + label;
                    checkAll(true);
                    if (!PipelineMultiPieceAssemblyTrial.IsExactIdentity(component.Transform2))
                    {
                        stage = "准备并核验" + label + "单位变换";
                        var identity = math.CreateTransform(new double[] {
                            1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0
                        }) as MathTransform ?? throw new InvalidOperationException("无法创建单位变换。");
                        PipelineStraightAssemblyTrial.RequireIdentity(math, identity);
                        if (!PipelineMultiPieceAssemblyTrial.IsExactIdentity(identity)) throw new InvalidOperationException("创建的单位变换不精确。");
                        checkAll(true);
                        stage = "校正新增组件" + label + "单位变换";
                        SolidWorksFileLogger.Info("多段已有装配体试验：只校正新增组件" + label + "的单位变换。");
                        component.Transform2 = identity;
                    }
                    stage = "插入后完整核验" + label;
                    checkAll(false);
                    SolidWorksFileLogger.Info("多段已有装配体试验：组件" + label + "为浮动且引用、位置和原组件基线复验通过。");
                }
                stage = "最终核验全部组件";
                checkAll(false);
                SolidWorksFileLogger.Info("多段已有装配体试验完成；未保存、重建、配合或验证几何连接及干涉，不能防止外部并发变化。");
                return new PipelineMultiPieceExistingAssemblyTrialResult(source, originalTitle, configuration, saved, inserted);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is SecurityException || ex is COMException ||
                ex is InvalidComObjectException || ex is Win32Exception || ex is NotSupportedException ||
                ex is ArithmeticException || ex is InvalidCastException || ex is NullReferenceException ||
                ex is CryptographicException)
            {
                var failure = new PipelineMultiPieceExistingAssemblyTrialException(stage, originalTitle, paths,
                    activationAttempted, attempted, inserted.Count, ex);
                SolidWorksFileLogger.Error(failure.Message + "；异常类型=" + ex.GetType().Name);
                throw failure;
            }
            finally { foreach (var guard in guards) guard.Dispose(); }
        }

        private static bool Same(SldWorks app, object first, object second) =>
            app.IsSame(first, second) == (int)swObjectEquality.swObjectSame;

        private static bool PathEquals(string first, string second) =>
            !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
            string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

        private static void RequireContext(SldWorks app, IntPtr window)
        {
            PipelineStraightModelBuilder.RequireUiThread(window);
            var frame = app.Frame() as Frame;
            if (frame == null || new IntPtr(frame.GetHWndx64()) != window)
                throw new InvalidOperationException("SolidWorks 主窗口已改变或不可用。");
            app.GetRunningCommandInfo(out int command, out string name, out bool active);
            if (active) throw new InvalidOperationException("SolidWorks 存在活动命令或属性管理器。");
            PipelineStraightModelBuilder.RequireUiThread(window);
        }

        private static void RequireUniqueTitle(SldWorks app, ModelDoc2 source, string title)
        {
            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            PipelineThreePieceModelTrial.RequireOpen(app, open, source);
            int matches = 0;
            foreach (object item in open)
            {
                var document = item as ModelDoc2 ?? throw new InvalidOperationException("无法确认打开文档身份。");
                if (!string.Equals(document.GetTitle(), title, StringComparison.OrdinalIgnoreCase)) continue;
                matches++;
                if (!Same(app, document, source)) throw new InvalidOperationException("装配体标题不唯一。");
            }
            if (matches != 1) throw new InvalidOperationException("原装配体标题不唯一或已关闭。");
        }

        private static void CheckSaved(SldWorks app, ModelDoc2 source, IntPtr window,
            PipelineMultiPieceSavedResult saved, ModelDoc2[] parts,
            List<PipelineMultiPieceSaveProtection.FileGuard> guards, bool insertionAttempted)
        {
            RequireContext(app, window);
            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            PipelineThreePieceModelTrial.RequireOpen(app, open, source);
            // 插入可能使原装配体变脏；此前必须与保存时完全一致，之后不允许已有脏状态被清除。
            bool dirty = source.GetSaveFlag();
            if (source.GetType() != saved.SourceType || source.GetTitle() != saved.SourceTitle ||
                source.GetPathName() != saved.SourcePath ||
                source.ConfigurationManager?.ActiveConfiguration?.Name != saved.SourceConfiguration ||
                (!insertionAttempted && dirty != saved.SourceDirty) || (saved.SourceDirty && !dirty))
                throw new InvalidOperationException("源装配体类型、标题、路径、配置或保存状态已改变。");
            for (int i = 0; i < parts.Length; i++)
            {
                var item = saved.Parts[i];
                PipelineThreePieceModelTrial.RequireDifferent(app, source, parts[i]);
                for (int j = 0; j < i; j++)
                {
                    PipelineThreePieceModelTrial.RequireDifferent(app, parts[i], parts[j]);
                    if (PathEquals(item.Path, saved.Parts[j].Path))
                        throw new InvalidOperationException("零件路径不唯一。");
                }
                PipelineThreePieceModelTrial.RequireOpen(app, open, parts[i]);
                if (string.IsNullOrWhiteSpace(item.Path) || !PipelineStraightModelBuilder.IsFullyQualifiedPath(item.Path) ||
                    !string.Equals(Path.GetExtension(item.Path), ".sldprt", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(item.Path) || parts[i].GetType() != (int)swDocumentTypes_e.swDocPART ||
                    parts[i].GetSaveFlag() || !PathEquals(parts[i].GetPathName(), item.Path) ||
                    string.IsNullOrWhiteSpace(item.Title) || parts[i].GetTitle() != item.Title ||
                    string.IsNullOrWhiteSpace(item.Configuration) ||
                    parts[i].GetConfigurationByName(item.Configuration) == null ||
                    !string.Equals(parts[i].ConfigurationManager?.ActiveConfiguration?.Name, item.Configuration, StringComparison.Ordinal))
                    throw new InvalidOperationException("已保存零件的文件、身份、路径、标题、保存状态或配置已变化。");
                object opened = app.GetOpenDocumentByName(item.Path);
                if (opened == null || !Same(app, opened, parts[i]))
                    throw new InvalidOperationException("无法确认路径对应本次打开的零件。");
                guards[i].CheckSnapshot(saved.Files[i]);
            }
            RequireContext(app, window);
        }

        private static void RequireTarget(SldWorks app, ModelDoc2 source, AssemblyDoc assembly,
            string title, string configuration)
        {
            if (!Same(app, source, assembly) || source.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY ||
                !string.Equals(source.GetTitle(), title, StringComparison.Ordinal) ||
                source.IsOpenedReadOnly() || !string.IsNullOrEmpty(source.GetPathName()) ||
                !string.Equals(PipelineAssemblyRootContext.Require(app, source, assembly), configuration, StringComparison.Ordinal) ||
                source.GetConfigurationByName(configuration) == null ||
                source.SketchManager == null || source.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("原装配体身份、活动状态、路径、配置或编辑状态已变化。");
            var selection = source.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) != 0)
                throw new InvalidOperationException("装配体存在选择或无法确认选择状态。");
        }

    }
}

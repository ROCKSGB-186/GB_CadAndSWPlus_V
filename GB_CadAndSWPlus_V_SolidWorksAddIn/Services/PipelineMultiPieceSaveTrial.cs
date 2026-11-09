using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Windows.Threading;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class PipelineMultiPieceSavedPart
    {
        internal PipelineMultiPieceSavedPart(string label, string path, string title, string configuration, ModelDoc2 document)
        {
            Label = label; Path = path; Title = title; Configuration = configuration; Document = document;
        }
        public string Label { get; }
        public string Path { get; }
        public string Title { get; }
        public string Configuration { get; }
        public ModelDoc2 Document { get; }
    }

    internal sealed class PipelineMultiPieceSavedResult
    {
        [ThreadStatic]
        private static PipelineMultiPieceSavedResult? pending;
        private readonly SldWorks app;
        private readonly ModelDoc2 source;
        private readonly IntPtr window;
        private readonly int thread;
        private readonly object? scope;
        private int consumed;
        internal PipelineMultiPieceSavedResult(IList<PipelineMultiPieceSavedPart> parts, SldWorks app,
            ModelDoc2 source, IntPtr window, IList<object> observed, string sourceTitle, string sourcePath,
            int sourceType, bool sourceDirty, string? sourceConfiguration,
            IList<PipelineMultiPieceSaveProtection.FileGuard.Snapshot> files)
        {
            Parts = new ReadOnlyCollection<PipelineMultiPieceSavedPart>(new List<PipelineMultiPieceSavedPart>(parts));
            this.app = app; this.source = source; this.window = window;
            thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            scope = PipelineMultiPieceAssemblyTrial.CurrentScope;
            Observed = new ReadOnlyCollection<object>(new List<object>(observed));
            SourceTitle = sourceTitle; SourcePath = sourcePath; SourceType = sourceType;
            SourceDirty = sourceDirty; SourceConfiguration = sourceConfiguration;
            Files = new ReadOnlyCollection<PipelineMultiPieceSaveProtection.FileGuard.Snapshot>(
                new List<PipelineMultiPieceSaveProtection.FileGuard.Snapshot>(files));

            // 只允许同一个 UI 同步回调中的紧接装配；返回消息循环后使结果失效。
            // 新一次保存也会主动使上一次结果失效，不依赖计时器。
            pending = null;
            try
            {
                Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                    throw new InvalidOperationException("UI 消息调度器不可用。");
                pending = this;
                DispatcherOperation operation = dispatcher.BeginInvoke(DispatcherPriority.Send,
                    new Action(() => { if (ReferenceEquals(pending, this)) pending = null; }));
                if (operation.Status == DispatcherOperationStatus.Aborted)
                    throw new InvalidOperationException("无法限定保存结果的 UI 回调有效期。");
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception ||
                ex is NotSupportedException)
            {
                // 不影响单独保存功能；无法可靠限定有效期时禁止该结果参与装配。
                if (ReferenceEquals(pending, this)) pending = null;
                SolidWorksFileLogger.Warning("保存已完成，但无法限定装配结果有效期；禁止使用该结果装配；异常类型=" +
                    ex.GetType().Name + "。");
            }
        }
        public IReadOnlyList<PipelineMultiPieceSavedPart> Parts { get; }
        internal IReadOnlyList<object> Observed { get; }
        internal IReadOnlyList<PipelineMultiPieceSaveProtection.FileGuard.Snapshot> Files { get; }
        internal string SourceTitle { get; }
        internal string SourcePath { get; }
        internal int SourceType { get; }
        internal bool SourceDirty { get; }
        internal string? SourceConfiguration { get; }

        internal static void InvalidatePending() { pending = null; }

        // 保存结果只允许在原 UI 回调、原线程、原会话中用于一次装配尝试。
        internal void Consume(SldWorks currentApp, ModelDoc2 currentSource, IntPtr currentWindow)
        {
            if (System.Threading.Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("保存结果已经用于装配尝试，不能重复使用。");
            if (thread != System.Threading.Thread.CurrentThread.ManagedThreadId || window != currentWindow ||
                !ReferenceEquals(app, currentApp) || !ReferenceEquals(pending, this) ||
                (scope != null && !ReferenceEquals(scope, PipelineMultiPieceAssemblyTrial.CurrentScope)))
                throw new InvalidOperationException("保存结果已过期，或不属于当前线程、窗口及 SolidWorks 会话。");
            pending = null;
            PipelineStraightModelBuilder.RequireUiThread(window);
            if (app.IsSame(source, currentSource) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("保存结果不属于当前源文档。");
        }
    }

    internal sealed class PipelineMultiPieceSaveTrialException : Exception
    {
        internal PipelineMultiPieceSaveTrialException(string stage, string message, Exception inner)
            : base(message, inner) { Stage = stage; }
        public string Stage { get; }
    }

    /// <summary>用户确认期间持有目录防护；取消时必须 Dispose，保存结束也会自动释放。</summary>
    internal sealed class PipelineMultiPieceSaveConfirmation : IDisposable
    {
        private readonly object sync = new object();
        private readonly PipelineMultiPieceSaveProtection.DirectoryGuard guard;
        private bool inUse, disposed;

        internal PipelineMultiPieceSaveConfirmation(string[] paths, PipelineMultiPieceSaveProtection.DirectoryGuard guard)
        {
            Paths = new ReadOnlyCollection<string>((string[])paths.Clone());
            this.guard = guard;
        }

        public IReadOnlyList<string> Paths { get; }

        internal PipelineMultiPieceSaveProtection.DirectoryGuard Begin()
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(PipelineMultiPieceSaveConfirmation));
                if (inUse) throw new InvalidOperationException("确认上下文已在使用。");
                inUse = true;
                return guard;
            }
        }

        internal void Finish()
        {
            lock (sync)
            {
                if (!inUse) throw new InvalidOperationException("确认上下文未在使用。");
                inUse = false;
                disposed = true;
                guard.Dispose();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (inUse) throw new InvalidOperationException("保存试验进行中，不能释放确认上下文。");
                if (disposed) return;
                disposed = true;
                guard.Dispose();
            }
        }
    }

    /// <summary>
    /// 同步试验：调用方须事先获得用户确认，并确保确认到结束期间无人访问或修改专用目录和文件。
    /// saver 为兼容 SolidWorks 允许共享写入；指纹与身份检查不是独占锁，也不能排除检查间的竞态。
    /// 不装配、不写属性或数据库，不关闭文档、删除用户文件或自动回滚。
    /// 为保护空目录会创建仅属于本试验的临时哨兵，释放保护时由系统删除该哨兵。
    /// </summary>
    internal static class PipelineMultiPieceSaveTrial
    {
        private sealed class Item
        {
            internal string Label = "";
            internal string Path = "";
            internal string? Title;
            internal string? Configuration;
            internal ModelDoc2? Document;
            internal bool Verified;
            internal PipelineMultiPieceSaveProtection.FileGuard? File;
        }

        /// <summary>预检固定顺序路径并保护目录；取消时调用方必须释放返回的上下文。</summary>
        internal static PipelineMultiPieceSaveConfirmation PreparePaths(SldWorks app, string folder, PipelineMultiSegmentPlan plan)
        {
            try
            {
                if (app == null) throw new ArgumentNullException(nameof(app));
                // 只读取主窗口句柄；后续目录和打开文档检查均先验证进程和 UI 线程。
                var frame = app.Frame() as Frame ??
                    throw new InvalidOperationException("SolidWorks 主窗口不可用。");
                return PreparePaths(app, new IntPtr(frame.GetHWndx64()), folder, plan);
            }
            catch (PipelineMultiPieceSaveTrialException) { throw; }
            catch (Exception ex) when (Expected(ex))
            {
                const string stage = "目标路径预检";
                SolidWorksFileLogger.Error("多段保存目标预检失败；异常类型=" + ex.GetType().Name + "。");
                throw new PipelineMultiPieceSaveTrialException(stage, Describe(stage, new List<Item>(), false, ex), ex);
            }
        }

        /// <summary>已知主窗口句柄时，可在任何 SolidWorks 调用前验证进程和 UI 线程。</summary>
        internal static PipelineMultiPieceSaveConfirmation PreparePaths(SldWorks app, IntPtr window, string folder, PipelineMultiSegmentPlan plan)
        {
            var items = new List<Item>();
            PipelineMultiPieceSaveProtection.DirectoryGuard? guard = null;
            try
            {
                if (app == null) throw new ArgumentNullException(nameof(app));
                RequireContext(app, window);
                if (plan == null) throw new ArgumentNullException(nameof(plan));
                if (plan.Straights.Count == 0 || plan.ElbowPlans.Count != plan.Straights.Count - 1 ||
                    plan.ElbowPointNumbers.Count != plan.ElbowPlans.Count)
                    throw new InvalidOperationException("多段方案的零件数量不一致。");
                MakeItems(items, folder, plan);
                guard = PipelineMultiPieceSaveProtection.DirectoryGuard.Acquire(Path.GetDirectoryName(items[0].Path)!);
                guard.CheckEntries(new string[0]);
                CheckRemainingTargets(app, items, 0);
                guard.CheckEntries(new string[0]);
                RequireContext(app, window);
                var paths = new List<string>();
                foreach (Item item in items) paths.Add(item.Path);
                var confirmation = new PipelineMultiPieceSaveConfirmation(paths.ToArray(), guard);
                SolidWorksFileLogger.Info("多段保存目标预检通过；专用目录除本试验哨兵外为空；目录保护持续至取消或保存结束。已保存且保持打开的目标允许对应的普通锁文件，锁文件名称不证明所有权；须确保无人并发访问或写入，共享写入不能提供绝对防覆盖保证。");
                guard = null;
                return confirmation;
            }
            catch (Exception ex) when (Expected(ex))
            {
                string message = Describe("目标路径预检", items, false, ex);
                SolidWorksFileLogger.Error("多段保存目标预检失败；异常类型=" + ex.GetType().Name + "。");
                throw new PipelineMultiPieceSaveTrialException("目标路径预检", message, ex);
            }
            finally { guard?.Dispose(); }
        }

        /// <summary>在 SolidWorks 主窗口所属进程的 UI 线程执行；仅返回逐件保存及复验结果。</summary>
        internal static PipelineMultiPieceSavedResult BuildAndSave(SldWorks app, ModelDoc2 source, IntPtr window,
            string template, PipelineMultiSegmentPlan plan, PipelineMultiPieceSaveConfirmation confirmation)
        {
            string stage = "输入和线程预检";
            string operation = Guid.NewGuid().ToString("N");
            var items = new List<Item>();
            var observed = new List<object>();
            bool started = false;
            Item? current = null;
            PipelineMultiPieceSaveProtection.DirectoryGuard? guard = null;
            try
            {
                SolidWorksFileLogger.Info("多段独立零件保存试验开始；编号=" + operation + "。");
                if (confirmation == null) throw new ArgumentNullException(nameof(confirmation));
                guard = confirmation.Begin();
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (source == null) throw new ArgumentNullException(nameof(source));
                PipelineStraightModelBuilder.RequireUiThread(window);
                PipelineMultiPieceSavedResult.InvalidatePending();
                var requested = new List<string>(confirmation.Paths);
                template = PipelineMultiPieceModelTrial.ValidatePlan(template, plan, step =>
                {
                    stage = step;
                    SolidWorksFileLogger.Info("编号=" + operation + "；阶段=" + stage + "。");
                });
                stage = "预检目标路径";
                if (requested.Count == 0 || string.IsNullOrWhiteSpace(requested[0]))
                    throw new ArgumentException("必须提供经用户确认的目标路径。", nameof(confirmation));
                string folder = Path.GetDirectoryName(requested[0]) ??
                    throw new ArgumentException("目标目录不可用。", nameof(confirmation));
                MakeItems(items, folder, plan);
                if (requested.Count != items.Count)
                    throw new ArgumentException("目标路径数量与方案不一致；用户确认的路径=" +
                        string.Join("、", requested), nameof(confirmation));
                for (int i = 0; i < items.Count; i++)
                    if (!PathsEqual(requested[i], items[i].Path))
                        throw new ArgumentException("目标路径与确认的专用目录、名称或路线顺序不一致；提供的路径=" +
                            requested[i], nameof(confirmation));
                CheckFilesAndDirectory(guard, items);
                CheckRemainingTargets(app, items, 0);
                CheckFilesAndDirectory(guard, items);
                stage = "检查源文档和启动条件";
                string sourceTitle = source.GetTitle();
                string sourcePath = source.GetPathName();
                int sourceType = source.GetType();
                bool sourceDirty = source.GetSaveFlag();
                string? sourceConfiguration = source.ConfigurationManager?.ActiveConfiguration?.Name;
                if (string.IsNullOrWhiteSpace(sourceTitle))
                    throw new InvalidOperationException("源文档标题不可用。");
                CheckState(app, source, window, items, observed, source, sourceTitle, sourcePath, sourceType, sourceDirty, -1);

                for (int index = 0; index < items.Count; index++)
                {
                    current = items[index];
                    int piece = index / 2;
                    stage = "创建前检查" + current.Label;
                    SolidWorksFileLogger.Info("编号=" + operation + "；诊断：开始" + stage + "。");
                    ModelDoc2 previous = index == 0 ? source : items[index - 1].Document!;
                    CheckState(app, source, window, items, observed, previous, sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                    SolidWorksFileLogger.Info("编号=" + operation + "；诊断：首次创建前状态检查完成。");
                    CheckRemainingTargets(app, items, index);
                    CheckFilesAndDirectory(guard, items);
                    SolidWorksFileLogger.Info("编号=" + operation + "；诊断：创建前路径和文件检查完成，开始再次检查状态。");
                    CheckState(app, source, window, items, observed, previous, sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                    SolidWorksFileLogger.Info("编号=" + operation + "；诊断：再次创建前状态检查完成。");
                    stage = "创建" + current.Label;
                    SolidWorksFileLogger.Info("编号=" + operation + "；阶段=" + stage + "；目标路径=" + current.Path + "。");
                    started = true;
                    PipelineStraightModelResult? straight = null;
                    PipelineElbowModelResult? elbow = null;
                    // 新文档返回后、任何建模写操作之前再次核对所有已观察身份和上下文。
                    Action<ModelDoc2, IReadOnlyList<object>> verifyNew = (document, existing) =>
                    {
                        PipelineMultiPieceModelTrial.VerifyNewDocument(app, window, observed, existing, document);
                        CheckState(app, source, window, items, observed, document, sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                        CheckFilesAndDirectory(guard, items);
                    };
                    if ((index & 1) == 0)
                    {
                        straight = PipelineStraightModelBuilder.Build(app, window, template, plan.Straights[piece],
                            plan.OutsideDiameterMm, plan.WallThicknessMm, verifyNew);
                        current.Title = straight?.Title;
                        current.Document = straight?.Document;
                        if (straight == null || straight.Document == null || straight.Feature == null)
                            throw new InvalidOperationException("直管生成结果不完整。");
                    }
                    else
                    {
                        elbow = PipelineElbowModelBuilder.Build(app, window, template, plan.ElbowPlans[piece], verifyNew);
                        current.Title = elbow?.Title;
                        current.Document = elbow?.Document;
                        if (elbow == null || elbow.Document == null || elbow.Feature == null || elbow.PathFeature == null || elbow.Segment == null)
                            throw new InvalidOperationException("弯头生成结果不完整。");
                    }
                    ModelDoc2 documentToSave = current.Document!;
                    if (string.IsNullOrWhiteSpace(current.Title))
                        throw new InvalidOperationException("新零件标题不可用。");
                    current.Configuration = ConfigurationName(documentToSave);
                    stage = "保存前检查" + current.Label;
                    CheckState(app, source, window, items, observed, documentToSave, sourceTitle, sourcePath, sourceType, sourceDirty, index);
                    CheckRemainingTargets(app, items, index);
                    CheckFilesAndDirectory(guard, items);
                    CheckState(app, source, window, items, observed, documentToSave, sourceTitle, sourcePath, sourceType, sourceDirty, index);
                    stage = "保存并复验" + current.Label;
                    Item savedItem = current;
                    PipelineStraightModelSaver.Save(app, documentToSave, window, savedItem.Path, savedItem.Label, () =>
                    {
                        try
                        {
                            savedItem.Title = RequiredTitle(documentToSave);
                            CheckState(app, source, window, items, observed, documentToSave, sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                            CheckSavedFiles(items);
                            savedItem.File = PipelineMultiPieceSaveProtection.FileGuard.Capture(savedItem.Path);
                            if (elbow != null)
                                PipelineElbowModelBuilder.Revalidate(app, elbow, plan.ElbowPlans[piece], savedItem.Path);
                            else
                                PipelineStraightModelBuilder.Revalidate(app, straight ?? throw new InvalidOperationException("直管结果不可用。"),
                                    plan.Straights[piece], plan.OutsideDiameterMm, plan.WallThicknessMm);
                            CheckState(app, source, window, items, observed, documentToSave, sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                        }
                        catch (Exception ex) when (ex is ArithmeticException || ex is NullReferenceException || ex is InvalidCastException)
                        {
                            // 让 saver 在复验异常时仍核验预留文件身份和指纹。
                            throw new InvalidOperationException("保存后复验发生计算或互操作错误。", ex);
                        }
                    });
                    stage = "保存后检查" + current.Label;
                    CheckState(app, source, window, items, observed, documentToSave, sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                    CheckFilesAndDirectory(guard, items);
                    current.Verified = true;
                    SolidWorksFileLogger.Info("编号=" + operation + "；" + current.Label + "保存复验通过；标题=" +
                        current.Title + "；路径=" + current.Path + "。");
                }
                stage = "最终复核目录和全部已保存文件";
                CheckFilesAndDirectory(guard, items);
                CheckState(app, source, window, items, observed, items[items.Count - 1].Document!,
                    sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                CheckFilesAndDirectory(guard, items);
                var result = new List<PipelineMultiPieceSavedPart>();
                var snapshots = new List<PipelineMultiPieceSaveProtection.FileGuard.Snapshot>();
                foreach (Item item in items)
                {
                    result.Add(new PipelineMultiPieceSavedPart(item.Label, item.Path, item.Title!, item.Configuration!, item.Document!));
                    snapshots.Add((item.File ?? throw new InvalidOperationException("保存文件保护不可用。")).CaptureSnapshot());
                }
                if (source.ConfigurationManager?.ActiveConfiguration?.Name != sourceConfiguration)
                    throw new InvalidOperationException("源文档活动配置已改变。");
                CheckState(app, source, window, items, observed, items[items.Count - 1].Document!,
                    sourceTitle, sourcePath, sourceType, sourceDirty, -1);
                SolidWorksFileLogger.Info("编号=" + operation + "；多段逐件保存复验完成；未装配、写属性数据库或关闭文档。");
                return new PipelineMultiPieceSavedResult(result, app, source, window, observed,
                    sourceTitle, sourcePath, sourceType, sourceDirty, sourceConfiguration, snapshots);
            }
            catch (Exception ex) when (Expected(ex))
            {
                if (current != null)
                {
                    if (ex is PipelineStraightModelBuildException straight && !string.IsNullOrWhiteSpace(straight.DocumentTitle))
                        current.Title = straight.DocumentTitle;
                    if (ex is PipelineElbowModelBuildException elbow && !string.IsNullOrWhiteSpace(elbow.DocumentTitle))
                        current.Title = elbow.DocumentTitle;
                }
                string message = Describe(stage, items, started, ex);
                SolidWorksFileLogger.Error("编号=" + operation + "；" + Describe(stage, items, started, null) +
                    "；异常类型=" + ex.GetType().Name + "。");
                throw new PipelineMultiPieceSaveTrialException(stage, message, ex);
            }
            finally
            {
                foreach (Item item in items) item.File?.Dispose();
                if (guard != null) confirmation.Finish();
            }
        }

        private static void MakeItems(List<Item> result, string folder, PipelineMultiSegmentPlan plan)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("必须选择已存在的专用空文件夹。", nameof(folder));
            for (int i = 0; i < plan.Straights.Count; i++)
            {
                AddItem(result, folder, "直管 " + (i + 1), "直管_" + (i + 1).ToString("D4") + ".sldprt");
                if (i < plan.ElbowPlans.Count)
                    AddItem(result, folder, "弯头 " + (i + 1) + "（原路线点 " + plan.ElbowPointNumbers[i] + "）",
                        "弯头_" + (i + 1).ToString("D4") + ".sldprt");
            }
        }

        private static void AddItem(List<Item> items, string folder, string label, string name)
        {
            string path = PipelineStraightModelSaver.ValidatePath(Path.Combine(folder, name));
            foreach (Item item in items)
                if (PathsEqual(item.Path, path)) throw new InvalidOperationException("目标路径重复。");
            items.Add(new Item { Label = label, Path = path });
        }

        private static void CheckSavedFiles(List<Item> items)
        {
            foreach (Item item in items)
                if (item.File != null) item.File.Check();
        }

        private static void CheckFilesAndDirectory(PipelineMultiPieceSaveProtection.DirectoryGuard guard, List<Item> items)
        {
            var expected = new List<string>();
            foreach (Item item in items)
                if (item.File != null) expected.Add(item.Path);
            guard.CheckEntries(expected);
            CheckSavedFiles(items);
            guard.CheckEntries(expected);
        }

        private static void CheckRemainingTargets(SldWorks app, List<Item> items, int first)
        {
            for (int i = first; i < items.Count; i++)
            {
                string path = PipelineStraightModelSaver.ValidatePath(items[i].Path);
                if (!PathsEqual(path, items[i].Path) || File.Exists(path) || Directory.Exists(path) ||
                    app.GetOpenDocumentByName(path) != null)
                    throw new IOException("尚未保存的目标路径已存在、已打开或发生变化：" + items[i].Path);
            }
        }

        private static void CheckState(SldWorks app, ModelDoc2 source, IntPtr window, List<Item> items,
            List<object> observed, ModelDoc2 active, string sourceTitle, string sourcePath, int sourceType,
            bool sourceDirty, int unsavedIndex)
        {
            SolidWorksFileLogger.Info("多段保存诊断：开始检查 SolidWorks 上下文。");
            RequireContext(app, window);
            SolidWorksFileLogger.Info("多段保存诊断：上下文检查完成，开始读取打开的文档。");
            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            SolidWorksFileLogger.Info("多段保存诊断：打开的文档读取完成，开始核验文档状态。");
            PipelineThreePieceModelTrial.RequireOpen(app, open, source);
            if (source.GetType() != sourceType || source.GetTitle() != sourceTitle ||
                source.GetPathName() != sourcePath || source.GetSaveFlag() != sourceDirty)
                throw new InvalidOperationException("源文档类型、标题、路径或保存状态已改变。");
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.Document == null) continue;
                ModelDoc2 document = item.Document;
                PipelineThreePieceModelTrial.RequireDifferent(app, source, document);
                for (int j = 0; j < i; j++)
                    if (items[j].Document != null) PipelineThreePieceModelTrial.RequireDifferent(app, items[j].Document!, document);
                PipelineThreePieceModelTrial.RequireOpen(app, open, document);
                if (document.GetType() != (int)swDocumentTypes_e.swDocPART ||
                    document.GetTitle() != item.Title || ConfigurationName(document) != item.Configuration)
                    throw new InvalidOperationException("已创建零件的类型、标题或活动配置已改变。");
                if (i == unsavedIndex)
                {
                    if (!string.IsNullOrEmpty(document.GetPathName()))
                        throw new InvalidOperationException("待保存零件已有路径。");
                }
                else
                {
                    string actualPath = document.GetPathName();
                    if (!PathsEqual(actualPath, item.Path) || document.GetSaveFlag())
                        throw new InvalidOperationException("已保存零件路径或保存状态已改变；观察到路径=" +
                            (string.IsNullOrWhiteSpace(actualPath) ? "不可用" : actualPath));
                }
            }
            PipelineStraightModelBuilder.RequireActive(app, active);
            RequireContext(app, window);
            PipelineStraightModelBuilder.RequireActive(app, active);
            PipelineMultiPieceModelTrial.Remember(observed, source);
            foreach (object document in open) PipelineMultiPieceModelTrial.Remember(observed, document);
            SolidWorksFileLogger.Info("多段保存诊断：文档状态检查完成。");
        }

        private static void RequireContext(SldWorks app, IntPtr window)
        {
            PipelineStraightModelBuilder.RequireUiThread(window);
            var frame = app.Frame() as Frame;
            if (frame == null || new IntPtr(frame.GetHWndx64()) != window)
                throw new InvalidOperationException("SolidWorks 主窗口已改变或不可用。");
            app.GetRunningCommandInfo(out int command, out string commandTitle, out bool activeCommand);
            if (activeCommand) throw new InvalidOperationException("存在活动命令。");
            PipelineStraightModelBuilder.RequireUiThread(window);
        }

        private static string RequiredTitle(ModelDoc2 document)
        {
            string title = document.GetTitle();
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("零件标题不可用。");
            return title;
        }

        private static string ConfigurationName(ModelDoc2 document)
        {
            string name = document.ConfigurationManager?.ActiveConfiguration?.Name ??
                throw new InvalidOperationException("无法读取零件的活动配置。");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("无法读取零件的活动配置。");
            return name;
        }

        private static bool PathsEqual(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
                string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Expected(Exception ex)
        {
            return ex is PipelineMultiPieceSaveTrialException || ex is PipelineStraightModelBuildException ||
                ex is PipelineElbowModelBuildException || ex is PipelineStraightModelSaveException ||
                ex is PipelineElbowModelRevalidationException || ex is COMException || ex is InvalidComObjectException ||
                ex is InvalidOperationException || ex is ArgumentException || ex is ArithmeticException ||
                ex is NotSupportedException || ex is IOException || ex is UnauthorizedAccessException ||
                ex is SecurityException || ex is Win32Exception || ex is CryptographicException ||
                ex is NullReferenceException || ex is InvalidCastException;
        }

        private static string Describe(string stage, List<Item> items, bool started, Exception? ex)
        {
            var details = new List<string>();
            foreach (Item item in items)
                details.Add(item.Label + "：标题=" + (item.Title ?? "未知或尚未创建") + "，目标路径=" + item.Path +
                    "，完整保存复验=" + (item.Verified ? "通过" : "未确认"));
            return "多段保存试验失败；阶段=" + stage + (ex == null ? "" : "；原因=" + ex.Message) + "；" +
                (details.Count == 0 ? "目标路径不可用" : string.Join("；", details)) +
                (started ? "。可能存在未知标题、路径的残留文档或文件，未确认不代表未写入；已复验结果可能随后变化。" :
                    "。本阶段未尝试创建零件或写入零件文件；可能已创建内部临时目录哨兵。") +
                "本试验非事务，未自动关闭文档、删除用户文件或回滚；仅释放本试验自建临时哨兵，请人工检查现场。";
        }
    }
}

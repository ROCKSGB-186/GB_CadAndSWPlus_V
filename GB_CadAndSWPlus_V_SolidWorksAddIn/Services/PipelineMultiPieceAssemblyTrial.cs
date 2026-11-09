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
    internal sealed class PipelineMultiPieceAssemblyTrialException : Exception
    {
        internal PipelineMultiPieceAssemblyTrialException(string stage, IList<string> paths, string? title,
            bool created, int attempted, int inserted, Exception inner)
            : base("多段装配试验失败；阶段=" + stage + "；原因=" + inner.Message +
                "；本次保存文件=" + string.Join("、", paths) + "；装配体标题=" + (title ?? "未知") +
                "；新建已尝试=" + (created ? "是" : "否") + "；插入尝试=" + attempted + "；已返回组件=" + inserted +
                "。可能存在未知文档或组件残留；未自动保存、关闭、删除或回滚，请人工检查；不能防止外部并发变化。", inner)
        {
            Stage = stage; AssemblyTitle = title; CreationAttempted = created;
            InsertionAttempted = attempted; InsertedCount = inserted;
            SavedPaths = new ReadOnlyCollection<string>(new List<string>(paths));
        }
        public string Stage { get; }
        public string? AssemblyTitle { get; }
        public bool CreationAttempted { get; }
        public int InsertionAttempted { get; }
        public int InsertedCount { get; }
        public IReadOnlyList<string> SavedPaths { get; }
    }

    internal sealed class PipelineMultiPieceAssemblyTrialResult
    {
        internal PipelineMultiPieceAssemblyTrialResult(string title, ModelDoc2 document, string configuration,
            PipelineMultiPieceSavedResult saved, IList<Component2> components, IList<bool> fixedStates)
        {
            AssemblyTitle = title; Document = document; Configuration = configuration; Saved = saved;
            Components = new ReadOnlyCollection<Component2>(new List<Component2>(components));
            FixedStates = new ReadOnlyCollection<bool>(new List<bool>(fixedStates));
        }
        public string AssemblyTitle { get; }
        public ModelDoc2 Document { get; }
        public string Configuration { get; }
        public PipelineMultiPieceSavedResult Saved { get; }
        public IReadOnlyList<Component2> Components { get; }
        public IReadOnlyList<bool> FixedStates { get; }
    }

    /// <summary>仅接受同次保存的单次结果；同步在 SolidWorks UI 线程执行，不回滚。</summary>
    internal static class PipelineMultiPieceAssemblyTrial
    {
        [ThreadStatic]
        private static object? currentScope;
        internal static object? CurrentScope => currentScope;

        /// <summary>
        /// 同一同步回调内调用 BuildAndSave 和 Build；离开回调后保存结果不可再用于装配。
        /// 用户确认必须在进入回调前完成，不可在回调内等待异步操作。
        /// </summary>
        internal static T WithFreshSave<T>(Func<T> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (currentScope != null) throw new InvalidOperationException("多段装配试验不能嵌套。");
            currentScope = new object();
            try { return operation(); }
            finally { currentScope = null; }
        }

        /// <summary>
        /// 同一 UI 同步回调中先调用 BuildAndSave，再立即将其结果传入此方法；
        /// 期间不可显示对话框、等待消息循环、await 或执行其他保存。
        /// 用户须已确认新建未保存装配体及插入、必要时校正本次组件变换。
        /// 保存结果仅可使用一次；不保证隔离外部并发操作。
        /// </summary>
        internal static PipelineMultiPieceAssemblyTrialResult Build(SldWorks app, ModelDoc2 source, IntPtr window,
            string assemblyTemplate, PipelineMultiPieceSavedResult saved)
        {
            string stage = "输入预检";
            string? title = null;
            bool creationAttempted = false;
            int attempted = 0;
            var inserted = new List<Component2>();
            var fixedStates = new List<bool>();
            var paths = new List<string>();
            var guards = new List<PipelineMultiPieceSaveProtection.FileGuard>();
            try
            {
                if (saved != null)
                    foreach (var item in saved.Parts) paths.Add(item.Path);
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (saved == null) throw new ArgumentNullException(nameof(saved));
                saved.Consume(app, source, window);
                if (saved.Parts.Count == 0 || saved.Parts.Count != saved.Files.Count)
                    throw new InvalidOperationException("本次保存结果不完整。");
                if (string.IsNullOrWhiteSpace(assemblyTemplate) ||
                    !PipelineStraightModelBuilder.IsFullyQualifiedPath(assemblyTemplate) ||
                    !string.Equals(Path.GetExtension(assemblyTemplate), ".asmdot", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(assemblyTemplate))
                    throw new ArgumentException("必须指定存在的装配体模板 .asmdot 绝对路径。", nameof(assemblyTemplate));
                int count = saved.Parts.Count;
                var parts = new ModelDoc2[count];
                var configurations = new string[count];
                var pathArray = paths.ToArray();
                for (int i = 0; i < count; i++)
                {
                    var item = saved.Parts[i];
                    if (item == null || item.Document == null || string.IsNullOrWhiteSpace(item.Label))
                        throw new InvalidOperationException("本次保存零件记录不完整。");
                    parts[i] = item.Document;
                    configurations[i] = item.Configuration;
                }
                var observed = new List<object>(saved.Observed);
                Action checkSaved = () =>
                {
                    CheckParts(app, source, window, parts, pathArray, configurations);
                    if (source.GetType() != saved.SourceType || source.GetTitle() != saved.SourceTitle ||
                        source.GetPathName() != saved.SourcePath || source.GetSaveFlag() != saved.SourceDirty ||
                        source.ConfigurationManager?.ActiveConfiguration?.Name != saved.SourceConfiguration)
                        throw new InvalidOperationException("源文档类型、标题、路径、配置或保存状态已改变。");
                    for (int i = 0; i < count; i++)
                    {
                        if (parts[i].GetTitle() != saved.Parts[i].Title)
                            throw new InvalidOperationException("已保存零件标题已改变。");
                        guards[i].CheckSnapshot(saved.Files[i]);
                    }
                };
                stage = "保护并检查本次保存文件";
                for (int i = 0; i < count; i++)
                {
                    var guard = PipelineMultiPieceSaveProtection.FileGuard.Capture(pathArray[i]);
                    guards.Add(guard);
                    guard.CheckSnapshot(saved.Files[i]);
                }
                checkSaved();
                PipelineStraightModelBuilder.RequireActive(app, parts[count - 1]);
                List<object> existing = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "已有文档列表");
                foreach (object item in existing)
                {
                    if (!(item is ModelDoc2)) throw new InvalidOperationException("无法确认已有文档身份。");
                    PipelineMultiPieceModelTrial.Remember(observed, item);
                }
                PipelineMultiPieceModelTrial.Remember(observed, source);
                foreach (ModelDoc2 part in parts) PipelineMultiPieceModelTrial.Remember(observed, part);
                checkSaved();
                PipelineStraightModelBuilder.RequireActive(app, parts[count - 1]);
                if (!File.Exists(assemblyTemplate)) throw new IOException("装配体模板在新建前已不可用。");
                stage = "新建空测试装配体";
                creationAttempted = true;
                var document = app.NewDocument(assemblyTemplate, 0, 0, 0) as ModelDoc2;
                if (document == null) throw new InvalidOperationException("新建未返回装配体，可能存在未知文档。");
                // 返回后、任何写操作前与保存期间及新建前见过的每个文档比较，未知身份一律拒绝。
                PipelineStraightModelBuilder.RequireUiThread(window);
                foreach (object previous in observed) PipelineThreePieceModelTrial.RequireDifferent(app, previous, document);
                title = document.GetTitle();
                if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("新装配体标题不可用。");
                var assembly = document as AssemblyDoc ?? throw new InvalidOperationException("新建文档没有装配体接口。");
                if (!Same(app, assembly, document)) throw new InvalidOperationException("无法确认新装配体接口身份。");
                string configuration = PipelineAssemblyRootContext.Require(app, document, assembly);
                var math = app.GetMathUtility() as MathUtility ?? throw new InvalidOperationException("无法获取数学工具。");
                Action<bool> checkAll = allowNewest =>
                {
                    checkSaved();
                    foreach (object previous in observed) PipelineThreePieceModelTrial.RequireDifferent(app, previous, document);
                    CheckAll(app, source, window, parts, pathArray, configurations, document, assembly,
                        title, configuration, math, inserted, fixedStates, allowNewest);
                    // 每次枚举均记录新看到的其他文档；不能把本次装配体加入历史集合。
                    foreach (object candidate in PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表"))
                    {
                        int same = app.IsSame(candidate, document);
                        if (same == (int)swObjectEquality.swObjectNotSame)
                            PipelineMultiPieceModelTrial.Remember(observed, candidate);
                        else if (same != (int)swObjectEquality.swObjectSame)
                            throw new InvalidOperationException("无法确认打开文档身份。");
                    }
                    checkSaved();
                    PipelineStraightModelBuilder.RequireActive(app, document);
                };
                stage = "验证新建空装配体";
                checkAll(false);
                SolidWorksFileLogger.Info("多段测试装配体新建并验证为空；标题=" + title + "。");
                for (int i = 0; i < count; i++)
                {
                    string label = saved.Parts[i].Label;
                    stage = "插入前检查" + label;
                    checkAll(false);
                    var part = parts[i] as PartDoc ?? throw new InvalidOperationException("零件接口不可用。");
                    stage = "计算" + label + "米制实体包围盒";
                    double[] center = SolidBodyCenterMeters(part);
                    checkAll(false);
                    stage = "插入" + label;
                    attempted = i + 1;
                    SolidWorksFileLogger.Info("多段装配开始插入" + label + "；路径=" + paths[i] + "；失败不回滚。");
                    Component2 component = assembly.AddComponent5(pathArray[i],
                        (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                        string.Empty, false, string.Empty, center[0], center[1], center[2]);
                    if (component == null) throw new InvalidOperationException("插入未返回组件，可能存在未知残留。");
                    inserted.Add(component);
                    fixedStates.Add(component.IsFixed());
                    stage = "调整前核验" + label;
                    checkAll(true);
                    if (!IsExactIdentity(component.Transform2))
                    {
                        stage = "准备并核验" + label + "的单位变换";
                        var identity = math.CreateTransform(new double[] {
                            1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0
                        }) as MathTransform ?? throw new InvalidOperationException("无法创建单位变换。");
                        PipelineStraightAssemblyTrial.RequireIdentity(math, identity);
                        checkAll(true);
                        stage = "校正" + label + "根坐标";
                        SolidWorksFileLogger.Info("多段装配校正本次组件单位变换；组件=" + label + "；保留固定状态。");
                        component.Transform2 = identity;
                    }
                    stage = "插入后完整核验" + label;
                    checkAll(false);
                    SolidWorksFileLogger.Info("多段装配组件验证通过；组件=" + label + "；固定状态=" +
                        (fixedStates[i] ? "固定" : "浮动") + "。");
                }
                stage = "最终核验全部组件";
                checkAll(false);
                SolidWorksFileLogger.Info("多段装配完成；未保存、重建、添加配合或验证几何连接及干涉；无法防止外部并发变化。");
                return new PipelineMultiPieceAssemblyTrialResult(title, document, configuration, saved, inserted, fixedStates);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is SecurityException || ex is COMException ||
                ex is InvalidComObjectException || ex is Win32Exception || ex is NotSupportedException ||
                ex is ArithmeticException || ex is InvalidCastException || ex is NullReferenceException ||
                ex is CryptographicException)
            {
                var failure = new PipelineMultiPieceAssemblyTrialException(stage, paths, title,
                    creationAttempted, attempted, inserted.Count, ex);
                SolidWorksFileLogger.Error(failure.Message + "；异常类型=" + ex.GetType().Name);
                throw failure;
            }
            finally { foreach (var guard in guards) guard.Dispose(); }
        }

        private static bool Same(SldWorks app, object first, object second) =>
            app.IsSame(first, second) == (int)swObjectEquality.swObjectSame;

        private static bool IsExactIdentity(MathTransform? transform)
        {
            if (!(transform?.ArrayData is Array values) || values.Rank != 1 || values.Length != 16)
                throw new InvalidOperationException("无法确认组件的精确单位变换。");
            int first = values.GetLowerBound(0);
            bool identity = true;
            for (int i = 0; i < 13; i++)
            {
                if (!(values.GetValue(first + i) is double value) || double.IsNaN(value) || double.IsInfinity(value))
                    throw new InvalidOperationException("组件变换包含无效数值。");
                double expected = i == 0 || i == 4 || i == 8 || i == 12 ? 1 : 0;
                if (value != expected) identity = false;
            }
            return identity;
        }

        private static double[] SolidBodyCenterMeters(PartDoc part)
        {
            object bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false);
            if (!(bodies is Array array) || array.Rank != 1 || array.Length != 1 ||
                !(array.GetValue(array.GetLowerBound(0)) is Body2 body) ||
                body.GetType() != (int)swBodyType_e.swSolidBody)
                throw new InvalidOperationException("零件必须恰有一个可读取的实体。");

            var minimum = new double[3];
            var maximum = new double[3];
            for (int axis = 0; axis < 3; axis++)
            {
                var direction = new double[3];
                direction[axis] = 1.0;
                maximum[axis] = ExtremeCoordinate(body, direction, axis);
                direction[axis] = -1.0;
                minimum[axis] = ExtremeCoordinate(body, direction, axis);
            }
            var center = new double[3];
            for (int axis = 0; axis < 3; axis++)
            {
                double span = maximum[axis] - minimum[axis];
                if (minimum[axis] > maximum[axis] || double.IsNaN(span) || double.IsInfinity(span))
                    throw new InvalidOperationException("实体包围盒极值顺序无效或范围溢出。");
                // 分别减半以避免同号极值相加时溢出。
                center[axis] = minimum[axis] / 2.0 + maximum[axis] / 2.0;
                if (double.IsNaN(center[axis]) || double.IsInfinity(center[axis]))
                    throw new InvalidOperationException("实体包围盒中心无效或溢出。");
            }
            return center;
        }

        private static double ExtremeCoordinate(Body2 body, double[] direction, int axis)
        {
            if (!body.GetExtremePoint(direction[0], direction[1], direction[2],
                out double x, out double y, out double z))
                throw new InvalidOperationException("无法读取实体方向极值点。");
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(z) || double.IsInfinity(z))
                throw new InvalidOperationException("实体极值点包含非有限坐标。");
            return axis == 0 ? x : axis == 1 ? y : z;
        }

        private static void CheckParts(SldWorks app, ModelDoc2 source, IntPtr window, ModelDoc2[] parts,
            string[] paths, string[] configurations)
        {
            PipelineStraightModelBuilder.RequireUiThread(window);
            var frame = app.Frame() as Frame;
            if (frame == null || new IntPtr(frame.GetHWndx64()) != window)
                throw new InvalidOperationException("SolidWorks 主窗口已改变或不可用。");
            RequireNoCommand(app);
            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            PipelineThreePieceModelTrial.RequireOpen(app, open, source);
            for (int i = 0; i < parts.Length; i++)
            {
                PipelineThreePieceModelTrial.RequireDifferent(app, source, parts[i]);
                for (int j = 0; j < i; j++)
                {
                    PipelineThreePieceModelTrial.RequireDifferent(app, parts[i], parts[j]);
                    if (PathEquals(paths[i], paths[j])) throw new InvalidOperationException("零件路径不唯一。");
                }
                PipelineThreePieceModelTrial.RequireOpen(app, open, parts[i]);
                if (string.IsNullOrWhiteSpace(paths[i]) || !PipelineStraightModelBuilder.IsFullyQualifiedPath(paths[i]) ||
                    !string.Equals(Path.GetExtension(paths[i]), ".sldprt", StringComparison.OrdinalIgnoreCase) || !File.Exists(paths[i]) ||
                    parts[i].GetType() != (int)swDocumentTypes_e.swDocPART || parts[i].GetSaveFlag() ||
                    !PathEquals(parts[i].GetPathName(), paths[i]) || string.IsNullOrWhiteSpace(parts[i].GetTitle()) ||
                    string.IsNullOrWhiteSpace(configurations[i]) || parts[i].GetConfigurationByName(configurations[i]) == null ||
                    !string.Equals(parts[i].ConfigurationManager?.ActiveConfiguration?.Name, configurations[i], StringComparison.Ordinal))
                    throw new InvalidOperationException("已保存零件的文件、身份、路径、保存状态或配置已变化。");
                object opened = app.GetOpenDocumentByName(paths[i]);
                if (opened == null || !Same(app, opened, parts[i]))
                    throw new InvalidOperationException("无法确认路径对应本次打开的零件。");
            }
            RequireNoCommand(app);
        }

        private static void RequireNoCommand(SldWorks app)
        {
            app.GetRunningCommandInfo(out int command, out string name, out bool active);
            if (active) throw new InvalidOperationException("SolidWorks 存在活动命令或属性管理器。");
        }

        private static bool PathEquals(string first, string second) =>
            !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
            string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

        private static void CheckAll(SldWorks app, ModelDoc2 source, IntPtr window, ModelDoc2[] parts,
            string[] paths, string[] configurations, ModelDoc2 document, AssemblyDoc assembly, string title,
            string configuration, MathUtility math, List<Component2> inserted, List<bool> fixedStates,
            bool allowNewestNonIdentity = false)
        {
            if (fixedStates.Count != inserted.Count || (allowNewestNonIdentity && inserted.Count == 0))
                throw new InvalidOperationException("无法确认组件固定状态基线。");
            CheckParts(app, source, window, parts, paths, configurations);
            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            PipelineThreePieceModelTrial.RequireOpen(app, open, document);
            if (!Same(app, document, assembly) || !string.Equals(document.GetTitle(), title, StringComparison.Ordinal) ||
                document.IsOpenedReadOnly() || !string.IsNullOrEmpty(document.GetPathName()) ||
                !string.Equals(PipelineAssemblyRootContext.Require(app, document, assembly), configuration, StringComparison.Ordinal) ||
                document.GetConfigurationByName(configuration) == null ||
                document.SketchManager == null || document.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("新装配体身份、活动状态、路径、配置或编辑状态已变化。");
            var selection = document.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) != 0)
                throw new InvalidOperationException("装配体存在选择或无法确认选择状态。");
            if (assembly.HasUnloadedComponents() || assembly.GetComponentCount(false) != inserted.Count ||
                assembly.GetComponentCount(true) != inserted.Count)
                throw new InvalidOperationException("装配体组件数量不符或存在未加载组件。");
            CheckComponentEnumeration(app, assembly.GetComponents(false), inserted);
            CheckComponentEnumeration(app, assembly.GetComponents(true), inserted);
            for (int i = 0; i < inserted.Count; i++)
            {
                Component2 component = inserted[i];
                if (component.GetSuppression2() != (int)swComponentSuppressionState_e.swComponentFullyResolved ||
                    component.IsGraphicsOnly || component.IsVirtual || component.HasUnloadedComponents() ||
                    component.GetHiddenUnloadedChildrenCount() != 0 || !PathEquals(component.GetPathName(), paths[i]) ||
                    !string.Equals(component.ReferencedConfiguration, configurations[i], StringComparison.Ordinal) ||
                    component.GetModelDoc2() == null || !Same(app, component.GetModelDoc2(), parts[i]) ||
                    component.IsFixed() != fixedStates[i])
                    throw new InvalidOperationException("组件解析状态、引用、路径、配置或固定状态不符。");
                if (!allowNewestNonIdentity || i != inserted.Count - 1)
                {
                    PipelineStraightAssemblyTrial.RequireIdentity(math, component.Transform2);
                    if (!IsExactIdentity(component.Transform2))
                        throw new InvalidOperationException("组件并非精确单位变换，无法确认其根坐标。");
                }
            }
            RequireNoMates(document, inserted);
            // COM 可重入；末尾重查活动文档和命令，但无法提供并发隔离。
            RequireNoCommand(app);
            PipelineStraightModelBuilder.RequireActive(app, document);
        }

        private static void CheckComponentEnumeration(SldWorks app, object? value, List<Component2> expected)
        {
            if (value == null)
            {
                if (expected.Count != 0) throw new InvalidOperationException("组件枚举数量不符。");
                return;
            }
            if (!(value is Array array) || array.Rank != 1)
                throw new InvalidOperationException("组件枚举不是一维数组。");
            List<object> actual = PipelineStraightModelBuilder.ReadComObjects(value, "装配体组件列表");
            if (actual.Count != expected.Count) throw new InvalidOperationException("组件枚举数量不符。");
            var seen = new bool[expected.Count];
            foreach (object item in actual)
            {
                if (!(item is Component2)) throw new InvalidOperationException("组件接口不可用。");
                int match = -1;
                for (int i = 0; i < expected.Count; i++)
                {
                    int same = app.IsSame(item, expected[i]);
                    if (same == (int)swObjectEquality.swObjectSame)
                    { if (match >= 0) throw new InvalidOperationException("组件身份重复。"); match = i; }
                    else if (same != (int)swObjectEquality.swObjectNotSame)
                        throw new InvalidOperationException("无法确认组件身份。");
                }
                if (match < 0 || seen[match]) throw new InvalidOperationException("出现未知或重复组件。");
                seen[match] = true;
            }
        }

        private static void RequireNoMates(ModelDoc2 document, List<Component2> components)
        {
            foreach (Component2 component in components)
            {
                object mates = component.GetMates();
                if (mates != null && (!(mates is Array array) || array.Rank != 1 || array.Length != 0))
                    throw new InvalidOperationException("组件存在配合或无法确认配合状态。");
            }
            object current = document.FirstFeature();
            if (current == null) throw new InvalidOperationException("无法读取装配体特征。");
            int count = 0, groups = 0;
            while (current != null)
            {
                if (++count > 100000 || !(current is Feature feature)) throw new InvalidOperationException("无法可靠遍历装配体特征。");
                string type = feature.GetTypeName2();
                if (string.IsNullOrEmpty(type)) throw new InvalidOperationException("无法确认特征类型。");
                if (string.Equals(type, "MateGroup", StringComparison.Ordinal))
                {
                    groups++;
                    if (feature.GetFirstSubFeature() != null) throw new InvalidOperationException("装配体配合组不为空。");
                }
                current = feature.GetNextFeature();
            }
            if (groups == 0) throw new InvalidOperationException("无法确认装配体配合组。");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class PipelineThreePieceAssemblyTrialException : Exception
    {
        internal PipelineThreePieceAssemblyTrialException(string stage, string[] paths, string? title,
            bool creationAttempted, int insertionAttempted, Exception inner)
            : base("三件装配试验失败；阶段=" + stage + "；原因=" + inner.Message +
                "；先前确认保存的文件=" + string.Join("、", paths) +
                "；已知装配体标题=" + (title ?? "未知") + "；最后尝试插入步骤=" + insertionAttempted +
                "；新建装配体已尝试=" + (creationAttempted ? "是" : "否") +
                "。文件、装配体或组件可能发生变化或存在未知残留；未自动保存装配体、关闭、删除或回滚，请人工检查。无法防止外部并发变更。", inner)
        {
            Stage = stage; AssemblyTitle = title; InsertionAttempted = insertionAttempted;
            CreationAttempted = creationAttempted;
            SavedPaths = new ReadOnlyCollection<string>((string[])paths.Clone());
        }
        public string Stage { get; }
        public string? AssemblyTitle { get; }
        public int InsertionAttempted { get; }
        public bool CreationAttempted { get; }
        public IReadOnlyList<string> SavedPaths { get; }
    }

    internal sealed class PipelineThreePieceAssemblyTrialResult
    {
        internal PipelineThreePieceAssemblyTrialResult(string title, ModelDoc2 document, string configuration,
            PipelineThreePieceSavedResult saved, Component2 inlet, Component2 elbow, Component2 outlet,
            IList<bool> fixedStates)
        {
            AssemblyTitle = title; Document = document; Configuration = configuration; Saved = saved;
            Inlet = inlet; Elbow = elbow; Outlet = outlet;
            FixedStates = new ReadOnlyCollection<bool>(new List<bool>(fixedStates));
        }
        public string AssemblyTitle { get; }
        internal ModelDoc2 Document { get; }
        internal string Configuration { get; }
        public PipelineThreePieceSavedResult Saved { get; }
        public Component2 Inlet { get; }
        public Component2 Elbow { get; }
        public Component2 Outlet { get; }
        internal IReadOnlyList<bool> FixedStates { get; }
    }

    /// <summary>仅在本次新建的未保存空装配体中插入；必要时只调整本次新增组件的变换，不更改固定状态。</summary>
    internal static class PipelineThreePieceAssemblyTrial
    {
        internal static PipelineThreePieceAssemblyTrialResult Build(SldWorks app, ModelDoc2 source, IntPtr window,
            string assemblyTemplate, PipelineThreePieceSavedResult saved)
        {
            string stage = "输入预检";
            string? title = null;
            bool creationAttempted = false;
            int attempted = 0;
            string[] paths = saved == null ? new string[0] : new[] { saved.InletPath, saved.ElbowPath, saved.OutletPath };
            try
            {
                SolidWorksFileLogger.Info("三件装配试验开始；不保存装配体、不加配合、不重建、不关闭、不回滚；无法防止外部并发变更。");
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (saved == null || saved.Inlet == null || saved.Elbow == null || saved.Outlet == null ||
                    saved.Inlet.Document == null || saved.Elbow.Document == null || saved.Outlet.Document == null ||
                    saved.Inlet.Feature == null || saved.Elbow.Feature == null || saved.Elbow.PathFeature == null ||
                    saved.Elbow.Segment == null || saved.Outlet.Feature == null)
                    throw new ArgumentException("保存结果不完整。", nameof(saved));
                if (string.IsNullOrWhiteSpace(assemblyTemplate) ||
                    !PipelineStraightModelBuilder.IsFullyQualifiedPath(assemblyTemplate) ||
                    !string.Equals(Path.GetExtension(assemblyTemplate), ".asmdot", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(assemblyTemplate))
                    throw new ArgumentException("必须指定存在的装配体模板 .asmdot 绝对路径。", nameof(assemblyTemplate));
                var parts = new[] { saved.Inlet.Document, saved.Elbow.Document, saved.Outlet.Document };
                var configurations = new[] { saved.InletConfiguration, saved.ElbowConfiguration, saved.OutletConfiguration };
                stage = "新建前检查源文档和三个已保存零件";
                CheckParts(app, source, window, parts, paths, configurations);
                List<object> existing = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "已有文档列表");
                foreach (object item in existing)
                    if (!(item is ModelDoc2)) throw new InvalidOperationException("无法确认已有文档身份。");
                PipelineThreePieceModelTrial.RequireOpen(app, existing, source);
                foreach (ModelDoc2 part in parts) PipelineThreePieceModelTrial.RequireOpen(app, existing, part);
                RequireNoCommand(app);
                if (!File.Exists(assemblyTemplate)) throw new IOException("装配体模板在新建前已不可用。");
                stage = "新建空测试装配体";
                creationAttempted = true;
                var document = app.NewDocument(assemblyTemplate, 0, 0, 0) as ModelDoc2;
                if (document == null) throw new InvalidOperationException("新建未返回装配体，可能存在未知文档。");
                title = document.GetTitle();
                foreach (object item in existing) PipelineThreePieceModelTrial.RequireDifferent(app, item, document);
                if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("新装配体标题不可用。");
                var assembly = document as AssemblyDoc ?? throw new InvalidOperationException("新建文档没有装配体接口。");
                if (!Same(app, assembly, document)) throw new InvalidOperationException("无法确认新装配体接口身份。");
                string configuration = PipelineAssemblyRootContext.Require(app, document, assembly);
                var math = app.GetMathUtility() as MathUtility ?? throw new InvalidOperationException("无法获取数学工具。");
                var inserted = new List<Component2>();
                var fixedStates = new List<bool>();
                stage = "验证新建空装配体";
                CheckAll(app, source, window, parts, paths, configurations, document, assembly, title, configuration, math, inserted, fixedStates);
                SolidWorksFileLogger.Info("新建装配体的身份、空状态及根级编辑状态验证通过；标题=" + title + "。");
                string[] labels = { "入直管", "弯头", "出直管" };
                for (int i = 0; i < 3; i++)
                {
                    stage = "插入前检查" + labels[i];
                    CheckAll(app, source, window, parts, paths, configurations, document, assembly, title, configuration, math, inserted, fixedStates);
                    var part = parts[i] as PartDoc ?? throw new InvalidOperationException("零件接口不可用。");
                    stage = "计算" + labels[i] + "的米制实体包围盒中心";
                    // 实体极值采用米；不能将受文档显示单位影响的 GetPartBox 直接传给 AddComponent5。
                    double[] center = SolidBodyCenterMeters(part);
                    CheckAll(app, source, window, parts, paths, configurations, document, assembly, title, configuration, math, inserted, fixedStates);
                    stage = "插入" + labels[i];
                    attempted = i + 1;
                    SolidWorksFileLogger.Info("开始插入" + labels[i] + "；路径=" + paths[i] + "；失败可能留下组件，不回滚。");
                    Component2 component = assembly.AddComponent5(paths[i],
                        (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                        string.Empty, false, string.Empty, center[0], center[1], center[2]);
                    if (component == null) throw new InvalidOperationException("插入未返回组件，可能存在未知残留。");
                    inserted.Add(component);
                    fixedStates.Add(component.IsFixed());
                    stage = "调整前核验" + labels[i];
                    // 只暂缓新组件的变换检查；身份、配合、固定状态及此前组件仍须完整通过。
                    CheckAll(app, source, window, parts, paths, configurations, document, assembly, title, configuration, math, inserted, fixedStates, true);
                    if (!PipelineStraightAssemblyTrial.IsIdentity(math, component.Transform2))
                    {
                        stage = "准备并核验" + labels[i] + "的单位变换";
                        var identity = math.CreateTransform(new double[] {
                            1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0
                        }) as MathTransform ?? throw new InvalidOperationException("无法创建单位变换。");
                        PipelineStraightAssemblyTrial.RequireIdentity(math, identity);
                        CheckAll(app, source, window, parts, paths, configurations, document, assembly, title, configuration, math, inserted, fixedStates, true);
                        stage = "校正" + labels[i] + "的根坐标";
                        SolidWorksFileLogger.Info("新组件位置非单位变换，开始仅校正" + labels[i] + "；保留固定状态，失败可能留下组件或位置变更，不回滚。");
                        component.Transform2 = identity;
                    }
                    stage = "插入后完整核验" + labels[i];
                    CheckAll(app, source, window, parts, paths, configurations, document, assembly, title, configuration, math, inserted, fixedStates);
                    SolidWorksFileLogger.Info(labels[i] + "插入后引用及根坐标单位变换验证通过；固定状态=" +
                        (fixedStates[i] ? "固定" : "浮动") + "；保留 SolidWorks 默认状态。");
                }
                stage = "最终核验全部组件";
                CheckAll(app, source, window, parts, paths, configurations, document, assembly, title, configuration, math, inserted, fixedStates);
                SolidWorksFileLogger.Info("三件身份、引用、配置及单位变换核验通过；未保存、重建或添加配合。未验证真实几何连接、干涉或三件同时最终几何；无法防止外部并发变更。");
                return new PipelineThreePieceAssemblyTrialResult(title!, document, configuration, saved,
                    inserted[0], inserted[1], inserted[2], fixedStates);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is SecurityException || ex is COMException ||
                ex is InvalidComObjectException || ex is Win32Exception || ex is NotSupportedException ||
                ex is ArithmeticException || ex is InvalidCastException || ex is NullReferenceException)
            {
                var failure = new PipelineThreePieceAssemblyTrialException(stage, paths, title, creationAttempted, attempted, ex);
                SolidWorksFileLogger.Error(failure.Message + "；异常类型=" + ex.GetType().Name);
                throw failure;
            }
        }

        internal static void VerifyState(SldWorks app, ModelDoc2 source, IntPtr window,
            PipelineThreePieceAssemblyTrialResult result)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (result == null) throw new ArgumentNullException(nameof(result));
            PipelineThreePieceSavedResult saved = result.Saved;
            var assembly = result.Document as AssemblyDoc ??
                throw new InvalidOperationException("本次测试装配体接口不可用。");
            var math = app.GetMathUtility() as MathUtility ??
                throw new InvalidOperationException("无法获取数学工具。");
            var parts = new[] { saved.Inlet.Document, saved.Elbow.Document, saved.Outlet.Document };
            var paths = new[] { saved.InletPath, saved.ElbowPath, saved.OutletPath };
            var configurations = new[] { saved.InletConfiguration, saved.ElbowConfiguration, saved.OutletConfiguration };
            var components = new List<Component2> { result.Inlet, result.Elbow, result.Outlet };
            CheckAll(app, source, window, parts, paths, configurations, result.Document, assembly,
                result.AssemblyTitle, result.Configuration, math, components, new List<bool>(result.FixedStates));
            // 组件实体坐标语义尚待复核；仅允许根坐标与零件坐标精确重合。
            foreach (Component2 component in components)
            {
                var values = component.Transform2?.ArrayData as Array;
                if (values == null || values.Rank != 1 || values.Length != 16)
                    throw new InvalidOperationException("无法确认组件的精确单位变换。");
                int first = values.GetLowerBound(0);
                for (int i = 0; i < 13; i++)
                {
                    double expected = i == 0 || i == 4 || i == 8 || i == 12 ? 1 : 0;
                    if (!(values.GetValue(first + i) is double value) || value != expected)
                        throw new InvalidOperationException("组件并非精确单位变换，不能使用零件坐标核验装配端口。");
                }
            }
        }

        private static bool Same(SldWorks app, object first, object second) =>
            app.IsSame(first, second) == (int)swObjectEquality.swObjectSame;

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
            for (int i = 0; i < 3; i++)
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
                    PipelineStraightAssemblyTrial.RequireIdentity(math, component.Transform2);
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

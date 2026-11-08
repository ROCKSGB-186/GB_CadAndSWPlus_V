using System;
using System.Collections.Generic;
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
    internal sealed class PipelineThreePieceSavedResult
    {
        internal PipelineThreePieceSavedResult(PipelineStraightModelResult inlet, PipelineElbowModelResult elbow,
            PipelineStraightModelResult outlet, string[] paths, string[] configurations)
        {
            Inlet = inlet; Elbow = elbow; Outlet = outlet;
            InletPath = paths[0]; ElbowPath = paths[1]; OutletPath = paths[2];
            InletConfiguration = configurations[0]; ElbowConfiguration = configurations[1]; OutletConfiguration = configurations[2];
        }

        public PipelineStraightModelResult Inlet { get; }
        public PipelineElbowModelResult Elbow { get; }
        public PipelineStraightModelResult Outlet { get; }
        public string InletPath { get; }
        public string ElbowPath { get; }
        public string OutletPath { get; }
        public string InletConfiguration { get; }
        public string ElbowConfiguration { get; }
        public string OutletConfiguration { get; }
    }

    internal sealed class PipelineThreePieceSaveTrialException : Exception
    {
        internal PipelineThreePieceSaveTrialException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>仅保存三个独立零件；调用方须先取得用户确认并确保保存期间无人访问文件。</summary>
    internal static class PipelineThreePieceSaveTrial
    {
        private static readonly string[] Names = { "第一根直管.sldprt", "弯头.sldprt", "第二根直管.sldprt" };
        private static readonly string[] Labels = { "入直管", "弯头", "出直管" };

        internal static string[] PreparePaths(SldWorks app, string folder)
        {
            var paths = new string[3];
            try
            {
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("必须选择已存在的专用空文件夹。", nameof(folder));
                for (int i = 0; i < paths.Length; i++)
                    paths[i] = PipelineStraightModelSaver.ValidatePath(Path.Combine(folder, Names[i]));
                for (int i = 0; i < paths.Length; i++)
                {
                    for (int j = 0; j < i; j++)
                        if (PathsEqual(paths[i], paths[j])) throw new InvalidOperationException("三个目标路径重复。");
                    if (File.Exists(paths[i]) || Directory.Exists(paths[i]) || app.GetOpenDocumentByName(paths[i]) != null)
                        throw new IOException("目标路径已存在或已被 SolidWorks 打开。");
                }
                using (var entries = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(paths[0])).GetEnumerator())
                    if (entries.MoveNext()) throw new IOException("所选专用文件夹不是空文件夹。");
                SolidWorksFileLogger.Info("三件保存目标预检通过：专用文件夹为空，三个固定路径均可用；保存期间不得并发访问文件。");
                return paths;
            }
            catch (Exception ex) when (Expected(ex))
            {
                string state = Describe("目标路径预检", paths, new string[3], new bool[3], false);
                SolidWorksFileLogger.Error(state + "；异常类型=" + ex.GetType().Name);
                throw new PipelineThreePieceSaveTrialException(state + "；原因=" + ex.Message, ex);
            }
        }

        internal static PipelineThreePieceSavedResult BuildAndSave(SldWorks app, ModelDoc2 source, IntPtr window,
            string template, PipelineThreePiecePlan plan, string[] paths)
        {
            string stage = "输入和线程预检";
            string[] targets = paths == null ? new string[0] : (string[])paths.Clone();
            var titles = new string?[3];
            var verified = new bool[3];
            var documents = new List<ModelDoc2>();
            var configurations = new List<string>();
            PipelineStraightModelResult? inletResult = null, outletResult = null;
            PipelineElbowModelResult? elbowResult = null;
            bool started = false;
            int current = -1;
            try
            {
                SolidWorksFileLogger.Info("三件独立零件安全保存试验开始；调用方须确保保存期间无人访问目标文件。");
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (source == null) throw new ArgumentNullException(nameof(source));
                PipelineStraightModelBuilder.RequireUiThread(window);
                if (plan == null) throw new ArgumentNullException(nameof(plan));
                PipelineElbowBuildGeometry.Create(plan);
                PipelineStraightModelBuilder.ValidateInput(template, plan.InletRoute, plan.OutsideDiameterMm, plan.WallThicknessMm);
                PipelineStraightModelBuilder.ValidateInput(template, plan.OutletRoute, plan.OutsideDiameterMm, plan.WallThicknessMm);
                if (targets.Length != 3 || string.IsNullOrWhiteSpace(targets[0]))
                    throw new ArgumentException("必须提供三个固定名称的目标路径。", nameof(paths));
                stage = "检查源文档和启动条件";
                CheckState(app, source, window, documents, configurations, targets, source, -1);
                stage = "写入前重新预检专用文件夹和目标路径";
                string[] prepared = PreparePaths(app, Path.GetDirectoryName(targets[0]));
                for (int i = 0; i < 3; i++)
                    if (string.IsNullOrWhiteSpace(targets[i]) ||
                        !PathsEqual(PipelineStraightModelSaver.ValidatePath(targets[i]), prepared[i]))
                        throw new ArgumentException("目标路径与专用文件夹的三个固定名称或顺序不一致。", nameof(paths));
                targets = prepared;
                CheckState(app, source, window, documents, configurations, targets, source, -1);

                for (int i = 0; i < 3; i++)
                {
                    current = i;
                    stage = "创建" + Labels[i];
                    started = true;
                    SolidWorksFileLogger.Info("开始创建" + Labels[i] + "；目标路径=" + targets[i] + "。");
                    PipelineStraightModelResult? straight = null;
                    PipelineElbowModelResult? elbow = null;
                    ModelDoc2 document;
                    if (i == 1)
                    {
                        elbow = PipelineElbowModelBuilder.Build(app, window, template, plan);
                        titles[i] = elbow?.Title;
                        if (elbow == null || elbow.Document == null || elbow.Feature == null || elbow.PathFeature == null || elbow.Segment == null)
                            throw new InvalidOperationException("弯头生成结果不完整。");
                        document = elbow.Document;
                    }
                    else
                    {
                        straight = PipelineStraightModelBuilder.Build(app, window, template,
                            i == 0 ? plan.InletRoute : plan.OutletRoute, plan.OutsideDiameterMm, plan.WallThicknessMm);
                        titles[i] = straight?.Title;
                        if (straight == null || straight.Document == null || straight.Feature == null)
                            throw new InvalidOperationException("直管生成结果不完整。");
                        document = straight.Document;
                    }
                    if (string.IsNullOrWhiteSpace(titles[i])) throw new InvalidOperationException("新零件标题不可用。");
                    stage = "记录" + Labels[i] + "身份和配置";
                    documents.Add(document);
                    configurations.Add(ConfigurationName(document));
                    stage = "保存前检查" + Labels[i];
                    CheckState(app, source, window, documents, configurations, targets, document, i);
                    // 第一件建模也可能触发重入；在首次写入前再次执行完整空目录预检。
                    if (i == 0)
                    {
                        string[] again = PreparePaths(app, Path.GetDirectoryName(targets[0]));
                        for (int j = 0; j < 3; j++)
                            if (!PathsEqual(targets[j], again[j])) throw new InvalidOperationException("目标路径在保存前发生变化。");
                    }
                    else CheckRemainingTargets(app, targets, i);
                    CheckState(app, source, window, documents, configurations, targets, document, i);
                    stage = "保存并复验" + Labels[i];
                    int index = i;
                    PipelineStraightModelSaver.Save(app, document, window, targets[i], Labels[i], () =>
                    {
                        try
                        {
                            CheckState(app, source, window, documents, configurations, targets, document, -1);
                            RememberTitle(document, titles, index);
                            if (index == 1)
                                PipelineElbowModelBuilder.Revalidate(app, elbow ??
                                    throw new InvalidOperationException("弯头生成结果不可用。"), plan, targets[index]);
                            else
                                PipelineStraightModelBuilder.Revalidate(app, straight ??
                                    throw new InvalidOperationException("直管生成结果不可用。"),
                                    index == 0 ? plan.InletRoute : plan.OutletRoute, plan.OutsideDiameterMm, plan.WallThicknessMm);
                            // 直管复验自身只要求非空路径，故回调前后都核对确切路径和保存标志。
                            CheckState(app, source, window, documents, configurations, targets, document, -1);
                        }
                        catch (Exception ex) when (ex is ArithmeticException || ex is NullReferenceException || ex is InvalidCastException)
                        {
                            // 转换为 saver 可识别的复验错误，使其仍执行失败后的文件身份和指纹检查。
                            throw new InvalidOperationException("保存后复验发生计算或互操作错误。", ex);
                        }
                    });
                    stage = "保存后检查" + Labels[i];
                    CheckState(app, source, window, documents, configurations, targets, document, -1);
                    RememberTitle(document, titles, i);
                    verified[i] = true;
                    if (i == 0) inletResult = straight;
                    else if (i == 1) elbowResult = elbow;
                    else outletResult = straight;
                    SolidWorksFileLogger.Info(Labels[i] + "完整保存复验通过；标题=" + titles[i] + "；目标路径=" + targets[i] + "。");
                }
                stage = "记录三个实际保存路径";
                string[] actualPaths = { documents[0].GetPathName(), documents[1].GetPathName(), documents[2].GetPathName() };
                CheckState(app, source, window, documents, configurations, targets, documents[2], -1);
                for (int i = 0; i < 3; i++)
                    if (!PathsEqual(actualPaths[i], targets[i])) throw new InvalidOperationException("实际保存路径已改变。");
                SolidWorksFileLogger.Info("三个独立零件逐件保存复验完成；未重开文件，未同时复验三件几何，也未装配、写属性数据库或关闭文档。");
                return new PipelineThreePieceSavedResult(inletResult!, elbowResult!, outletResult!,
                    actualPaths, configurations.ToArray());
            }
            catch (Exception ex) when (Expected(ex))
            {
                if (current >= 0)
                {
                    if (ex is PipelineStraightModelBuildException straight && !string.IsNullOrWhiteSpace(straight.DocumentTitle)) titles[current] = straight.DocumentTitle;
                    if (ex is PipelineElbowModelBuildException elbow && !string.IsNullOrWhiteSpace(elbow.DocumentTitle)) titles[current] = elbow.DocumentTitle;
                }
                string state = Describe(stage, targets, titles, verified, started);
                SolidWorksFileLogger.Error(state + "；异常类型=" + ex.GetType().Name);
                throw new PipelineThreePieceSaveTrialException(state + "；原因=" + ex.Message, ex);
            }
        }

        private static void CheckRemainingTargets(SldWorks app, string[] paths, int first)
        {
            // 不覆盖未来目标，且每次保存前重新检查本机磁盘和目录链。
            for (int i = first; i < paths.Length; i++)
            {
                string path = PipelineStraightModelSaver.ValidatePath(paths[i]);
                if (!PathsEqual(path, paths[i]) || File.Exists(path) || Directory.Exists(path) || app.GetOpenDocumentByName(path) != null)
                    throw new IOException("尚未保存的目标路径已存在、已打开或发生变化。");
            }
        }

        private static void CheckState(SldWorks app, ModelDoc2 source, IntPtr window, List<ModelDoc2> documents,
            List<string> configurations, string[] paths, ModelDoc2 expectedActive, int unsavedIndex)
        {
            PipelineStraightModelBuilder.RequireUiThread(window);
            var frame = app.Frame() as Frame;
            if (frame == null || new IntPtr(frame.GetHWndx64()) != window)
                throw new InvalidOperationException("SolidWorks 主窗口已改变或不可用。");
            app.GetRunningCommandInfo(out int command, out string commandTitle, out bool activeCommand);
            if (activeCommand) throw new InvalidOperationException("存在活动命令，不能继续保存试验。");
            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            PipelineThreePieceModelTrial.RequireOpen(app, open, source);
            for (int i = 0; i < documents.Count; i++)
            {
                ModelDoc2 document = documents[i];
                PipelineThreePieceModelTrial.RequireDifferent(app, source, document);
                for (int j = 0; j < i; j++) PipelineThreePieceModelTrial.RequireDifferent(app, documents[j], document);
                PipelineThreePieceModelTrial.RequireOpen(app, open, document);
                if (document.GetType() != (int)swDocumentTypes_e.swDocPART || string.IsNullOrWhiteSpace(document.GetTitle()) ||
                    !string.Equals(ConfigurationName(document), configurations[i], StringComparison.Ordinal))
                    throw new InvalidOperationException("已创建零件的类型、标题或活动配置不可用或已改变。");
                string path = document.GetPathName();
                if (i == unsavedIndex)
                {
                    if (!string.IsNullOrEmpty(path)) throw new InvalidOperationException("待保存零件已有路径。");
                }
                else if (!PathsEqual(path, paths[i]) || document.GetSaveFlag())
                    throw new InvalidOperationException("已保存零件的路径或保存状态已改变。");
            }
            PipelineStraightModelBuilder.RequireActive(app, expectedActive);
            app.GetRunningCommandInfo(out command, out commandTitle, out activeCommand);
            if (activeCommand) throw new InvalidOperationException("检查期间出现活动命令。");
            PipelineStraightModelBuilder.RequireActive(app, expectedActive);
        }

        private static void RememberTitle(ModelDoc2 document, string?[] titles, int index)
        {
            string title = document.GetTitle();
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("无法读取零件当前标题。");
            titles[index] = title;
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

        private static string Describe(string stage, string[] paths, string?[] titles, bool[] verified, bool started)
        {
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
                parts.Add(Labels[i] + "：标题=" + (string.IsNullOrWhiteSpace(titles[i]) ? "未知或尚未创建" : titles[i]) +
                    "，目标路径=" + (i >= paths.Length || string.IsNullOrWhiteSpace(paths[i]) ? "不可用" : paths[i]) +
                    "，完整保存复验=" + (verified[i] ? "通过" : "未确认"));
            return "三件保存试验失败；阶段=" + stage + "；" + string.Join("；", parts) +
                (started ? "。当前步骤可能残留文件或文档，异常不代表未写入；已通过的结果也可能随后变化。" : "。本阶段未尝试创建文档或写入文件。") +
                "本试验非事务，未自动关闭、删除或回滚；请人工检查现场。";
        }

        private static bool Expected(Exception ex)
        {
            return ex is PipelineThreePieceSaveTrialException || ex is PipelineStraightModelBuildException ||
                ex is PipelineElbowModelBuildException || ex is PipelineStraightModelSaveException ||
                ex is PipelineElbowModelRevalidationException || ex is COMException || ex is InvalidComObjectException ||
                ex is InvalidOperationException || ex is ArgumentException || ex is ArithmeticException ||
                ex is NotSupportedException || ex is IOException || ex is UnauthorizedAccessException ||
                ex is SecurityException || ex is Win32Exception || ex is CryptographicException ||
                ex is NullReferenceException || ex is InvalidCastException;
        }
    }
}

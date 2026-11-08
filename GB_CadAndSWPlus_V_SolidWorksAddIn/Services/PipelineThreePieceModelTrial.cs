using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class PipelineThreePieceModelTrialResult
    {
        internal PipelineThreePieceModelTrialResult(PipelineStraightModelResult inlet,
            PipelineElbowModelResult elbow, PipelineStraightModelResult outlet)
        {
            Inlet = inlet;
            Elbow = elbow;
            Outlet = outlet;
        }

        public PipelineStraightModelResult Inlet { get; }
        public PipelineElbowModelResult Elbow { get; }
        public PipelineStraightModelResult Outlet { get; }
    }

    internal sealed class PipelineThreePieceModelTrialException : Exception
    {
        internal PipelineThreePieceModelTrialException(string stage, IList<string> titles, Exception inner)
            : base("三点路线独立零件试运行失败；阶段=" + stage + "；原因=" + inner.Message +
                "。已知创建零件标题：" + (titles.Count == 0 ? "无" : string.Join("、", titles)) +
                "。这些文档的当前打开和保存状态需由用户检查；还可能存在标题未知的残留文档。" +
                "未自动保存、关闭或回滚。", inner)
        {
            Stage = stage;
            CreatedTitles = new ReadOnlyCollection<string>(new List<string>(titles));
        }

        public string Stage { get; }
        public IReadOnlyList<string> CreatedTitles { get; }
    }

    /// <summary>
    /// 同步执行三个独立未保存零件的几何试运行。调用方须事先获得用户明确确认。
    /// 不进行装配、最终同时几何复验、属性或数据库操作，也不保存、激活或关闭文档。
    /// </summary>
    internal static class PipelineThreePieceModelTrial
    {
        internal static PipelineThreePieceModelTrialResult Build(SldWorks app, ModelDoc2 sourceDocument,
            IntPtr mainWindowHandle, string partTemplatePath, PipelineThreePiecePlan plan)
        {
            string stage = "输入和线程预检";
            var titles = new List<string>();
            var created = new List<ModelDoc2>();
            try
            {
                SolidWorksFileLogger.Info("三点路线独立零件试运行开始；同步预检输入和主窗口线程。");
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (sourceDocument == null) throw new ArgumentNullException(nameof(sourceDocument));
                PipelineStraightModelBuilder.RequireUiThread(mainWindowHandle);
                if (string.IsNullOrWhiteSpace(partTemplatePath) ||
                    !PipelineStraightModelBuilder.IsFullyQualifiedPath(partTemplatePath) ||
                    !string.Equals(Path.GetExtension(partTemplatePath), ".prtdot", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(partTemplatePath))
                    throw new ArgumentException("必须指定存在的零件模板 .prtdot 绝对路径。", nameof(partTemplatePath));
                if (plan == null) throw new ArgumentNullException(nameof(plan));
                PipelineElbowBuildGeometry.Create(plan);
                // 两段直管均复用 builder 的完整输入校验，避免在可预见的参数错误后留下部分零件。
                PipelineStraightModelBuilder.ValidateInput(partTemplatePath, plan.InletRoute,
                    plan.OutsideDiameterMm, plan.WallThicknessMm);
                PipelineStraightModelBuilder.ValidateInput(partTemplatePath, plan.OutletRoute,
                    plan.OutsideDiameterMm, plan.WallThicknessMm);

                stage = "检查源文档和启动条件";
                CheckState(app, sourceDocument, mainWindowHandle, created, sourceDocument);
                SolidWorksFileLogger.Info("源文档、方案及启动条件预检通过；准备创建入直管。");

                stage = "创建入直管";
                PipelineStraightModelResult inlet = PipelineStraightModelBuilder.Build(app, mainWindowHandle,
                    partTemplatePath, plan.InletRoute, plan.OutsideDiameterMm, plan.WallThicknessMm);
                Record(inlet?.Document, inlet?.Title, titles, created);
                if (inlet == null || inlet.Feature == null)
                    throw new InvalidOperationException("入直管生成结果或特征不可用。");

                stage = "创建弯头前检查";
                CheckState(app, sourceDocument, mainWindowHandle, created, inlet.Document);
                SolidWorksFileLogger.Info("入直管仍为活动的未保存零件；准备创建弯头。");
                stage = "创建弯头";
                PipelineElbowModelResult elbow = PipelineElbowModelBuilder.Build(app, mainWindowHandle,
                    partTemplatePath, plan);
                Record(elbow?.Document, elbow?.Title, titles, created);
                if (elbow == null || elbow.Feature == null || elbow.PathFeature == null || elbow.Segment == null)
                    throw new InvalidOperationException("弯头生成结果或特征不可用。");

                stage = "创建出直管前检查";
                CheckState(app, sourceDocument, mainWindowHandle, created, elbow.Document);
                SolidWorksFileLogger.Info("此前零件仍打开且未保存，弯头为活动文档；准备创建出直管。");
                stage = "创建出直管";
                PipelineStraightModelResult outlet = PipelineStraightModelBuilder.Build(app, mainWindowHandle,
                    partTemplatePath, plan.OutletRoute, plan.OutsideDiameterMm, plan.WallThicknessMm);
                Record(outlet?.Document, outlet?.Title, titles, created);
                if (outlet == null || outlet.Feature == null)
                    throw new InvalidOperationException("出直管生成结果或特征不可用。");

                stage = "复核三个零件";
                CheckState(app, sourceDocument, mainWindowHandle, created, outlet.Document);
                SolidWorksFileLogger.Info("三个独立零件的身份、打开状态和未保存路径复核通过；未装配，也未同时复验几何。");
                return new PipelineThreePieceModelTrialResult(inlet, elbow, outlet);
            }
            catch (Exception ex) when (ex is PipelineStraightModelBuildException ||
                ex is PipelineElbowModelBuildException || ex is COMException ||
                ex is NullReferenceException || ex is InvalidCastException ||
                ex is InvalidComObjectException || ex is InvalidOperationException ||
                ex is ArgumentException || ex is ArithmeticException || ex is NotSupportedException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // builder 只在确认新文档身份后才提供标题；未知标题的残留也必须提示。
                if (ex is PipelineStraightModelBuildException straight)
                    AddTitle(titles, straight.DocumentTitle);
                else if (ex is PipelineElbowModelBuildException elbow)
                    AddTitle(titles, elbow.DocumentTitle);
                SolidWorksFileLogger.Error("三点路线独立零件试运行失败；阶段=" + stage +
                    "；异常类型=" + ex.GetType().Name + "；已知创建标题=" +
                    (titles.Count == 0 ? "无" : string.Join("、", titles)) +
                    "；可能存在未知残留，未自动关闭或回滚。");
                throw new PipelineThreePieceModelTrialException(stage, titles, ex);
            }
        }

        private static void AddTitle(List<string> titles, string? title)
        {
            if (title != null && !string.IsNullOrWhiteSpace(title)) titles.Add(title);
        }

        private static void Record(ModelDoc2? document, string? title, List<string> titles, List<ModelDoc2> created)
        {
            // 即使结果不完整，也先保留 builder 提供的标题，供失败后人工检查。
            AddTitle(titles, title);
            if (document == null || string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("生成结果的文档或标题不可用，可能存在未知残留。");
            created.Add(document);
        }

        private static void CheckState(SldWorks app, ModelDoc2 source, IntPtr window,
            List<ModelDoc2> created, ModelDoc2 expectedActive)
        {
            PipelineStraightModelBuilder.RequireUiThread(window);
            var frame = app.Frame() as Frame;
            if (frame == null || new IntPtr(frame.GetHWndx64()) != window)
                throw new InvalidOperationException("SolidWorks 主窗口已改变或不可用。");
            app.GetRunningCommandInfo(out int command, out string commandTitle, out bool activeCommand);
            if (activeCommand) throw new InvalidOperationException("存在活动命令，不能继续试运行。");

            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            // 每次创建前和最终复核时，都检查原文档仍然打开。
            RequireOpen(app, open, source);
            for (int i = 0; i < created.Count; i++)
            {
                ModelDoc2 document = created[i];
                RequireDifferent(app, source, document);
                for (int j = 0; j < i; j++) RequireDifferent(app, created[j], document);
                RequireOpen(app, open, document);
                if (document.GetType() != (int)swDocumentTypes_e.swDocPART ||
                    !string.IsNullOrEmpty(document.GetPathName()) ||
                    string.IsNullOrWhiteSpace(document.GetTitle()))
                    throw new InvalidOperationException("已创建零件不再是有标题的未保存零件。");
            }

            // 最后检查活动文档；COM 调用可重入，不能将检查与后续新建视作原子操作。
            object active = app.ActiveDoc;
            if (active == null || app.IsSame(active, expectedActive) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("活动文档不是预期的源文档或上一步零件。");
        }

        private static void RequireOpen(SldWorks app, List<object> open, ModelDoc2 document)
        {
            int matches = 0;
            foreach (object candidate in open)
            {
                int same = app.IsSame(candidate, document);
                if (same == (int)swObjectEquality.swObjectSame) matches++;
                else if (same != (int)swObjectEquality.swObjectNotSame)
                    throw new InvalidOperationException("无法确认打开文档的身份。");
            }
            if (matches != 1) throw new InvalidOperationException("预期文档未打开或身份不唯一。");
        }

        private static void RequireDifferent(SldWorks app, object first, object second)
        {
            if (app.IsSame(first, second) != (int)swObjectEquality.swObjectNotSame)
                throw new InvalidOperationException("无法确认零件与源文档及其他零件身份不同。");
        }
    }
}

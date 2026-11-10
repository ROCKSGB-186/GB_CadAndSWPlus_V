using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal sealed class PipelineMultiPieceModelTrialResult
    {
        internal PipelineMultiPieceModelTrialResult(IList<string> titles)
        {
            Titles = new ReadOnlyCollection<string>(new List<string>(titles));
        }

        public IReadOnlyList<string> Titles { get; }
    }

    internal sealed class PipelineMultiPieceModelTrialException : Exception
    {
        internal PipelineMultiPieceModelTrialException(string stage, IList<string> titles, Exception inner)
            : base("多段路线独立零件试运行失败；阶段=" + stage + "；原因=" + inner.Message +
                "。已知创建零件标题：" + (titles.Count == 0 ? "无" : string.Join("、", titles)) +
                "。请检查其当前打开和保存状态；可能存在标题未知的残留文档。" +
                "未自动保存、关闭或回滚。", inner)
        {
            Stage = stage;
            CreatedTitles = new ReadOnlyCollection<string>(new List<string>(titles));
        }

        public string Stage { get; }
        public IReadOnlyList<string> CreatedTitles { get; }
    }

    /// <summary>仅创建独立未保存试验零件，不装配、写属性、保存或关闭。</summary>
    internal static class PipelineMultiPieceModelTrial
    {
        internal static PipelineMultiPieceModelTrialResult Build(SldWorks app, ModelDoc2 source,
            IntPtr window, string template, PipelineMultiSegmentPlan plan)
        {
            string stage = "输入和线程预检";
            string operation = Guid.NewGuid().ToString("N");
            var titles = new List<string>();
            var created = new List<ModelDoc2>();
            var observed = new List<object>();
            try
            {
                SolidWorksFileLogger.Info("多段独立零件试运行开始；编号=" + operation + "；阶段=" + stage + "。");
                if (app == null) throw new ArgumentNullException(nameof(app));
                if (source == null) throw new ArgumentNullException(nameof(source));
                PipelineStraightModelBuilder.RequireUiThread(window);
                template = ValidatePlan(template, plan, step =>
                {
                    stage = step;
                    SolidWorksFileLogger.Info("编号=" + operation + "；阶段=" + stage + "。");
                });

                stage = "检查源文档和启动条件";
                // 源文档允许本来已保存；记录其原状态，之后不允许身份、路径或标题发生变化。
                PipelineStraightModelBuilder.RequireUiThread(window);
                string sourceTitle = source.GetTitle();
                string sourcePath = source.GetPathName();
                int sourceType = source.GetType();
                CheckState(app, source, window, created, titles, observed, source, sourceTitle, sourcePath, sourceType);
                SolidWorksFileLogger.Info("编号=" + operation + "；源文档及全部几何预检通过。");

                for (int i = 0; i < plan.Straights.Count; i++)
                {
                    stage = "创建直管 " + (i + 1);
                    SolidWorksFileLogger.Info("编号=" + operation + "；阶段=" + stage + "。");
                    PipelineStraightModelResult straight = PipelineStraightModelBuilder.Build(app, window, template,
                        plan.Straights[i], plan.OutsideDiameterMm, plan.WallThicknessMm,
                        (document, existing) => VerifyNewDocument(app, window, observed, existing, document));
                    Record(straight?.Document, straight?.Title, titles, created);
                    if (straight == null || straight.Feature == null)
                        throw new InvalidOperationException("直管生成结果或特征不可用。");
                    stage = "复核直管 " + (i + 1);
                    CheckState(app, source, window, created, titles, observed, straight.Document, sourceTitle, sourcePath, sourceType);
                    SolidWorksFileLogger.Info("编号=" + operation + "；阶段=" + stage + "；标题=" + straight.Title + "。");

                    if (i == plan.ElbowPlans.Count) continue;
                    stage = "创建弯头 " + (i + 1) + "（原路线点 " + plan.ElbowPointNumbers[i] + "）";
                    SolidWorksFileLogger.Info("编号=" + operation + "；阶段=" + stage + "。");
                    PipelineElbowModelResult elbow = PipelineElbowModelBuilder.Build(app, window, template, plan.ElbowPlans[i],
                        (document, existing) => VerifyNewDocument(app, window, observed, existing, document));
                    Record(elbow?.Document, elbow?.Title, titles, created);
                    if (elbow == null || elbow.Feature == null || elbow.PathFeature == null || elbow.Segment == null)
                        throw new InvalidOperationException("弯头生成结果或特征不可用。");
                    stage = "复核弯头 " + (i + 1) + "（原路线点 " + plan.ElbowPointNumbers[i] + "）";
                    CheckState(app, source, window, created, titles, observed, elbow.Document, sourceTitle, sourcePath, sourceType);
                    SolidWorksFileLogger.Info("编号=" + operation + "；阶段=" + stage + "；标题=" + elbow.Title + "。");
                }

                SolidWorksFileLogger.Info("编号=" + operation + "；多段独立零件试运行完成，均未保存；未装配或复验全局干涉。");
                return new PipelineMultiPieceModelTrialResult(titles);
            }
            catch (Exception ex) when (ex is PipelineStraightModelBuildException ||
                ex is PipelineElbowModelBuildException || ex is COMException ||
                ex is NullReferenceException || ex is InvalidCastException ||
                ex is InvalidComObjectException || ex is InvalidOperationException ||
                ex is ArgumentException || ex is ArithmeticException || ex is NotSupportedException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                // builder 只在确认新文档身份后提供标题；其他残留可能无法获知。
                if (ex is PipelineStraightModelBuildException straight) AddTitle(titles, straight.DocumentTitle);
                else if (ex is PipelineElbowModelBuildException elbow) AddTitle(titles, elbow.DocumentTitle);
                SolidWorksFileLogger.Error("编号=" + operation + "；多段独立零件试运行失败；阶段=" + stage +
                    "；异常类型=" + ex.GetType().Name + "；已知标题=" +
                    (titles.Count == 0 ? "无" : string.Join("、", titles)) +
                    "；可能存在未知残留，未自动保存、关闭或回滚。");
                throw new PipelineMultiPieceModelTrialException(stage, titles, ex);
            }
        }

        // 保存试验与未保存试运行共用全部几何预检。
        internal static string ValidatePlan(string template, PipelineMultiSegmentPlan plan, Action<string> reportStage)
        {
            if (string.IsNullOrWhiteSpace(template) ||
                !PipelineStraightModelBuilder.IsFullyQualifiedPath(template) ||
                template.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                template.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new ArgumentException("必须指定可靠的零件模板绝对路径。", nameof(template));
            template = Path.GetFullPath(template);
            if (!string.Equals(Path.GetExtension(template), ".prtdot", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(template))
                throw new ArgumentException("必须指定存在的零件模板 .prtdot 文件。", nameof(template));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.ApproximatePointNumbers.Count != 0)
                throw new InvalidOperationException("近似共线仅用于理论预览，不能建模；原路线点号=" +
                    string.Join("、", plan.ApproximatePointNumbers) + "。");
            if (plan.Straights.Count == 0 || plan.ElbowPlans.Count != plan.Straights.Count - 1 ||
                plan.Elbows.Count != plan.ElbowPlans.Count || plan.ElbowPointNumbers.Count != plan.ElbowPlans.Count)
                throw new InvalidOperationException("多段方案的直管和弯头数量不一致。");
            for (int i = 0; i < plan.Straights.Count; i++)
            {
                reportStage("预检直管 " + (i + 1));
                PipelineStraightModelBuilder.ValidateInput(template, plan.Straights[i],
                    plan.OutsideDiameterMm, plan.WallThicknessMm);
                ValidateStraightVerificationPrecision(plan.Straights[i], plan.OutsideDiameterMm,
                    plan.WallThicknessMm);
            }
            for (int i = 0; i < plan.ElbowPlans.Count; i++)
            {
                reportStage("预检弯头 " + (i + 1) + "（原路线点 " + plan.ElbowPointNumbers[i] + "）");
                PipelineThreePiecePlan elbow = plan.ElbowPlans[i];
                PipelineElbowBuildGeometry.Create(elbow);
                if (!ReferenceEquals(elbow.Elbow, plan.Elbows[i]) ||
                    elbow.OutsideDiameterMm != plan.OutsideDiameterMm ||
                    elbow.WallThicknessMm != plan.WallThicknessMm ||
                    elbow.InsideDiameterMm != plan.InsideDiameterMm ||
                    plan.Straights[i].Points[1] != elbow.Elbow.EntryTangent ||
                    plan.Straights[i + 1].Points[0] != elbow.Elbow.ExitTangent)
                    throw new InvalidOperationException("弯头与相邻直管或方案尺寸不一致。");
            }
            // 所有多段建模与保存入口共用此预检，无法判定时不得创建零件。
            reportStage("检查路线自身碰撞风险");
            PipelineRouteSelfCollision.Validate(plan);
            return template;
        }

        private static void AddTitle(List<string> titles, string? title)
        {
            if (title != null && !string.IsNullOrWhiteSpace(title)) titles.Add(title);
        }

        private static void ValidateStraightVerificationPrecision(PipeRoute route, double diameter, double wall)
        {
            // 与直管 builder 的 VerifyLine/VerifyBody 使用相同的米单位容差；
            // 这些校验原本在新建后才运行，须提前阻止可预见的精度失败。
            PipePoint3D start = route.Points[0], end = route.Points[1];
            double sx = start.X / 1000.0, sy = start.Y / 1000.0, sz = start.Z / 1000.0;
            double ex = end.X / 1000.0, ey = end.Y / 1000.0, ez = end.Z / 1000.0;
            double x = ex - sx, y = ey - sy, z = ez - sz;
            double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            double length = scale * Math.Sqrt((x / scale) * (x / scale) +
                (y / scale) * (y / scale) + (z / scale) * (z / scale));
            double coordinateScale = Math.Max(Math.Max(Math.Abs(sx), Math.Max(Math.Abs(sy), Math.Abs(sz))),
                Math.Max(Math.Abs(ex), Math.Max(Math.Abs(ey), Math.Abs(ez))));
            double precision = coordinateScale * 3.552713678800501e-15;
            double pathTolerance = Math.Max(length * 1e-8, precision);
            double outer = (diameter / 1000.0) / 2.0;
            double inner = (((diameter - wall) - wall) / 1000.0) / 2.0;
            double radiusTolerance = Math.Min(outer * 1e-6, (outer - inner) / 4.0);
            double axisTolerance = Math.Max(inner * 1e-6, precision);
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0 ||
                pathTolerance <= 0 || pathTolerance >= length / 2.0 ||
                axisTolerance <= 0 || axisTolerance >= inner / 2.0 || radiusTolerance <= 0)
                throw new ArithmeticException("米单位下直管路径、轴线或半径的校验精度不足。");
        }

        private static void Record(ModelDoc2? document, string? title, List<string> titles, List<ModelDoc2> created)
        {
            AddTitle(titles, title);
            if (document == null || string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException("生成结果的文档或标题不可用，可能存在未知残留。");
            created.Add(document);
        }

        private static void CheckState(SldWorks app, ModelDoc2 source, IntPtr window,
            List<ModelDoc2> created, List<string> titles, List<object> observed, ModelDoc2 expectedActive,
            string sourceTitle, string sourcePath, int sourceType)
        {
            PipelineStraightModelBuilder.RequireUiThread(window);
            var frame = app.Frame() as Frame;
            if (frame == null || new IntPtr(frame.GetHWndx64()) != window)
                throw new InvalidOperationException("SolidWorks 主窗口已改变或不可用。");
            app.GetRunningCommandInfo(out int command, out string commandTitle, out bool activeCommand);
            if (activeCommand) throw new InvalidOperationException("存在活动命令，不能继续试运行。");
            List<object> open = PipelineStraightModelBuilder.ReadComObjects(app.GetDocuments(), "打开的文档列表");
            PipelineThreePieceModelTrial.RequireOpen(app, open, source);
            if (source.GetType() != sourceType || source.GetTitle() != sourceTitle || source.GetPathName() != sourcePath)
                throw new InvalidOperationException("源文档的类型、标题或保存路径已改变。");
            for (int i = 0; i < created.Count; i++)
            {
                ModelDoc2 document = created[i];
                PipelineThreePieceModelTrial.RequireDifferent(app, source, document);
                for (int j = 0; j < i; j++)
                    PipelineThreePieceModelTrial.RequireDifferent(app, created[j], document);
                PipelineThreePieceModelTrial.RequireOpen(app, open, document);
                if (document.GetType() != (int)swDocumentTypes_e.swDocPART ||
                    !string.IsNullOrEmpty(document.GetPathName()) ||
                    document.GetTitle() != titles[i])
                    throw new InvalidOperationException("已创建零件的类型、标题或未保存状态已改变。");
            }
            object active = app.ActiveDoc;
            if (active == null || app.IsSame(active, expectedActive) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("活动文档不是预期的源文档或上一步零件。");
            // COM 调用可能重入；这些检查无法与下一次新建操作构成原子事务。
            PipelineStraightModelBuilder.RequireUiThread(window);
            Remember(observed, source);
            foreach (ModelDoc2 document in created) Remember(observed, document);
            foreach (object document in open) Remember(observed, document);
        }

        internal static void Remember(List<object> observed, object document)
        {
            // 仅去除相同引用；不同 COM 包装即使可能指向同一对象也应保留并逐一比较。
            foreach (object previous in observed)
                if (ReferenceEquals(previous, document)) return;
            observed.Add(document);
        }

        internal static void VerifyNewDocument(SldWorks app, IntPtr window,
            List<object> observed, IReadOnlyList<object> existing, ModelDoc2 document)
        {
            PipelineStraightModelBuilder.RequireUiThread(window);
            // builder 新建前的枚举也可能发现此前未知的文档，后续阶段仍须记住。
            foreach (object previous in existing) Remember(observed, previous);
            foreach (object previous in observed)
                PipelineThreePieceModelTrial.RequireDifferent(app, previous, document);
        }
    }
}

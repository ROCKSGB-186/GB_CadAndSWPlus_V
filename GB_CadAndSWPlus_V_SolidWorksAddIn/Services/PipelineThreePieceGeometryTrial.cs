using System;
using SolidWorks.Interop.sldworks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>仅核验当前窗口生成的三件测试装配体，不主动更改或保存文档。</summary>
    internal static class PipelineThreePieceGeometryTrial
    {
        internal static string Verify(SldWorks app, ModelDoc2 source, IntPtr window,
            PipelineThreePieceAssemblyTrialResult trial, PipelineThreePiecePlan plan, out bool complete)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (trial == null) throw new ArgumentNullException(nameof(trial));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            complete = false;
            PipelineThreePieceAssemblyTrial.VerifyState(app, source, window, trial);
            bool assemblyDirty = trial.Document.GetSaveFlag();
            var documents = new[] { trial.Document, trial.Saved.Inlet.Document,
                trial.Saved.Elbow.Document, trial.Saved.Outlet.Document };
            var stamps = new int[documents.Length];
            for (int i = 0; i < documents.Length; i++) stamps[i] = documents[i].GetUpdateStamp();
            bool stateFailure = false;
            Action guard = () =>
            {
                try
                {
                    PipelineThreePieceAssemblyTrial.VerifyState(app, source, window, trial);
                    if (trial.Document.GetSaveFlag() != assemblyDirty)
                        throw new InvalidOperationException("核验期间测试装配体的保存状态已变化。");
                    for (int i = 0; i < documents.Length; i++)
                        if (documents[i].GetUpdateStamp() != stamps[i])
                            throw new InvalidOperationException("核验期间装配体或零件更新戳已变化。");
                }
                catch (Exception ex) when (IsExpected(ex))
                {
                    stateFailure = true;
                    throw;
                }
            };
            guard();
            SolidWorksFileLogger.Info("开始核验三件测试装配体；标题=" + trial.AssemblyTitle +
                "；不主动保存、重建或移动组件。");
            var components = new[] { trial.Inlet, trial.Elbow, trial.Outlet };
            SolidWorksFileLogger.Info("开始核验两对真实实体端口。");
            string ports;
            bool portsComplete = false;
            try
            {
                ports = PipelineThreePiecePortVerifier.Verify(app, components, plan, guard, out portsComplete);
                guard();
                SolidWorksFileLogger.Info("两对组件实体端口检查结束；" + ports);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                if (stateFailure)
                    throw new InvalidOperationException("端口检查期间装配体状态无法确认，本次结果无效；原因：" + ex.Message, ex);
                guard();
                portsComplete = false;
                ports = "端口检查未完成，不能判定连接；原因：" + ex.Message;
                SolidWorksFileLogger.Error("三件端口检查未完成：" + ex);
            }
            var assembly = trial.Document as AssemblyDoc ??
                throw new InvalidOperationException("无法读取本次测试装配体接口。");
            SolidWorksFileLogger.Info("开始逐对调用 SolidWorks 干涉检查接口。");
            string interference;
            bool interferenceComplete = false;
            try
            {
                interference = PipelineThreePieceInterferenceVerifier.Verify(app, assembly, components, guard,
                    out interferenceComplete);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                if (stateFailure)
                    throw new InvalidOperationException("干涉检查期间装配体状态无法确认，本次结果无效；原因：" + ex.Message, ex);
                guard();
                interferenceComplete = false;
                interference = "干涉检查未完成，不能判定有无干涉；原因：" + ex.Message;
                SolidWorksFileLogger.Error("三件干涉检查未完成：" + ex);
            }
            guard();
            SolidWorksFileLogger.Info("三件干涉接口检查结束；" + interference);
            complete = portsComplete && interferenceComplete;
            return ports + "\n\n" + interference +
                "\n\n仅针对本次未保存的三件测试装配体；不代表工程规范验收。组件后续移动后须重新检查。" +
                "状态检查不能排除所有并发更改或尚未重建的几何变化。";
        }

        private static bool IsExpected(Exception ex) =>
            ex is InvalidOperationException || ex is ArgumentException || ex is ArithmeticException ||
            ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException ||
            ex is System.Runtime.InteropServices.InvalidComObjectException || ex is System.IO.IOException ||
            ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is AggregateException;
    }
}

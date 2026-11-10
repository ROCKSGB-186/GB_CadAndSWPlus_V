using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Threading;

internal static class PauseSnapshotRegression
{
    public static int Run(string assemblyPath)
    {
        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        Type sessionType = assembly.GetType("GB_CadAndSWPlus_V.SolidWorksAddIn.Services.PipelineRoutePickingSession", true)!;
        Type selectionType = assembly.GetType("GB_CadAndSWPlus_V.SolidWorksAddIn.Models.PipelineRouteSelectionSession", true)!;
        Type pointType = assembly.GetType("GB_CadAndSWPlus_V.SolidWorksAddIn.Models.PipePoint3D", true)!;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;

        void CheckPauseFailure(bool confirming)
        {
            object selection = Activator.CreateInstance(selectionType, true)!;
            object point = Activator.CreateInstance(pointType, 23d, 0d, 0d)!;
            object?[] add = { point, null };
            if (!(bool)selectionType.GetMethod("TryAddPoint")!.Invoke(selection, add)!)
                throw new InvalidOperationException("无法准备暂停草稿。");
            object session = FormatterServices.GetUninitializedObject(sessionType);
            var fixture = new FaceComRegression.Fixture { Active = false };
            void Set(string name, object value) => sessionType.GetField(name, fields)!.SetValue(session, value);
            Set("_selection", selection);
            Set("_dispatcher", Dispatcher.CurrentDispatcher);
            Set("_moveTimer", new DispatcherTimer());
            Set("_managedThreadId", Thread.CurrentThread.ManagedThreadId);
            Set("_started", true);
            Set("_confirming", confirming);
            Set("_application", fixture.Application);
            Set("_document", fixture.Document);
            try
            {
                sessionType.GetMethod("PauseForFaceSelection")!.Invoke(session, null);
                throw new InvalidOperationException("不可用会话不应允许暂停成功。");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
            object? saved = sessionType.GetField("_pausedSelection", fields)!.GetValue(session);
            if (saved == null) throw new InvalidOperationException("暂停检查失败后丢失草稿快照。");
            var points = (ICollection)selectionType.GetProperty("Points")!.GetValue(saved)!;
            if (points.Count != 1) throw new InvalidOperationException("暂停失败后的点列不完整。");
        }

        CheckPauseFailure(true);
        CheckPauseFailure(false);
        return 2;
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Controls;
using GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    internal static class SolidWorksFileLogger
    {
        public static void Info(string message) { }
        public static void Warning(string message) { }
        public static void Error(string message) { }
        public static void Start(string message) { }
        public static void Complete(string message) { }
    }

    public sealed class PipelinePropertiesConfigurationService
    {
        public IDictionary<string, string> Load(IEnumerable<PipelinePropertiesFieldDefinition> fields) =>
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public void Save(IEnumerable<PipelinePropertiesFieldDefinition> fields, IDictionary<string, string> values,
            bool overwriteInvalidConfiguration = false) { }
    }

    public sealed class PipelinePropertiesInvalidConfigurationException : Exception { }

    internal static class PipelineStraightParameters
    {
        public static bool TryPositiveNumber(string text, out double value) =>
            double.TryParse(text, out value) && value > 0;
    }
}

internal static class Program
{
    private static int _count;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _count++;
    }

    private static Dictionary<string, string> Values() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { _count++; return; }
        throw new InvalidOperationException(message);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        var start = Values();
        var end = Values();
        var current = Values();
        var editable = new HashSet<string>(new[] { "DN", "PN", "MEDIUM" }, StringComparer.OrdinalIgnoreCase);
        Check(PipelineEndpointReferencePlanner.Create(start, end, current, editable).Count == 0, "空端点不应生成建议");
        start["DN"] = " DN100 ";
        start["PN"] = "  ";
        start["MEDIUM"] = "水";
        start["PIPE_MATL"] = "设备材质";
        end["DN"] = "DN80";
        current["DN"] = "DN50";
        var changes = PipelineEndpointReferencePlanner.Create(start, end, current, editable);
        Check(changes.Count == 2, "只应建议非空的允许字段");
        Check(changes[0].Tag == "DN" && changes[0].Value == "DN100" && changes[0].Current == "DN50", "建议应保留现值并去除空格");
        Check(changes[0].ConflictsWithOther && changes[0].Other == "DN80", "两端不一致应标识");
        Check(!changes[1].ConflictsWithOther, "另一端空值不应视为冲突");
        Check(current["DN"] == "DN50", "生成建议不得修改管道");
        editable.Remove("DN");
        changes = PipelineEndpointReferencePlanner.Create(start, end, current, editable);
        Check(changes.Count == 1 && changes[0].Tag == "MEDIUM", "只读字段不得覆盖");
        var saved = new PipelineEndpointReferences(start, end);
        start["DN"] = "修改";
        end["DN"] = "修改";
        Check(saved.Start["DN"] == " DN100 " && saved.End["DN"] == "DN80", "端点参考应为独立快照");

        var selection = new PipelineRouteSelectionSession();
        var first = new PipePoint3D(1.25, 2.5, 3.75);
        var second = new PipePoint3D(100.5, 2.5, 3.75);
        Check(selection.TryAddPoint(first, out _), "可手动添加起点");
        Check(!selection.TryAddPoint(first, out _), "重复相邻点应拒绝");
        var paused = selection.CopySelecting();
        Check(paused.TryAddPoint(second, out _) && paused.Points.Count == 2 && selection.Points.Count == 1,
            "端面坐标加入暂停快照不得改动原路线");
        var resumed = paused.CopySelecting();
        Check(resumed.Points.Count == 2 && resumed.TryRemoveLastPoint(out _) && resumed.Points.Count == 1 &&
            paused.Points.Count == 2, "恢复或撤销不得破坏暂停快照");
        Check(selection.TryAddPoint(second, out _), "可手动添加终点");
        Check(selection.TryComplete(out PipeRoute? route, out _) && route != null &&
            route.StartKind == PipeEndpointKind.Free && route.EndKind == PipeEndpointKind.Free &&
            route.Points[0] == first && route.Points[1] == second, "手动点应保持精确坐标且两端自由");

        var manual = new PipelinePointConfirmWindow(first, null, false, true);
        var picked = new PipelinePointConfirmWindow(first, null, false);
        TextBox Input(PipelinePointConfirmWindow window, string name) =>
            (TextBox)typeof(PipelinePointConfirmWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Check(Input(manual, "_x").Text == "1.25" && Input(manual, "_z").Text == "3.75", "手动输入初始值不得自动取整");
        Check(Input(picked, "_x").Text == "0" && Input(picked, "_z").Text == "0", "原鼠标拾点默认取整行为应保持");
        manual.Close();
        picked.Close();

        var attributes = Values();
        attributes["DN"] = "DN50";
        var properties = new PipelinePropertiesWindow(null, attributes, null, true, route: route, endpointReferences: saved);
        var referencesField = typeof(PipelinePropertiesWindow).GetField("_startReferences", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var inputs = (Dictionary<string, TextBox>)referencesField.GetValue(properties)!;
        Check(inputs["DN"].Text == " DN100 ", "属性窗口应恢复当前路线的端点参考快照");
        var editorsField = typeof(PipelinePropertiesWindow).GetField("_editors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pipeInputs = (Dictionary<string, Control>)editorsField.GetValue(properties)!;
        Check(((TextBox)pipeInputs["DN"]).Text == "DN50", "端点参考不得自动覆盖管道属性");
        Check(properties.ConfirmedEndpointReferences == null, "确认前不能产生端点参考结果");
        properties.Close();

        string prompt = string.Empty;
        bool accept = false;
        properties = new PipelinePropertiesWindow(null, attributes, null, true, route: route, endpointReferences: saved,
            confirmEndpointReferences: (message, title) => { prompt = message; return accept; });
        inputs = (Dictionary<string, TextBox>)referencesField.GetValue(properties)!;
        var endInputs = (Dictionary<string, TextBox>)typeof(PipelinePropertiesWindow)
            .GetField("_endReferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(properties)!;
        pipeInputs = (Dictionary<string, Control>)editorsField.GetValue(properties)!;
        var apply = typeof(PipelinePropertiesWindow).GetMethod("ApplyEndpointReferences", BindingFlags.Instance | BindingFlags.NonPublic)!;
        apply.Invoke(properties, new object[] { "起点", inputs, endInputs });
        Check(prompt.Contains("DN50 → DN100") && prompt.Contains("DN80（不一致）"), "确认提示应显示旧值、建议值及两端冲突");
        Check(((TextBox)pipeInputs["DN"]).Text == "DN50", "拒绝采用参考时不能修改管道");
        accept = true;
        apply.Invoke(properties, new object[] { "起点", inputs, endInputs });
        Check(((TextBox)pipeInputs["DN"]).Text == "DN100" && ((TextBox)pipeInputs["MEDIUM"]).Text == "水",
            "用户确认后应仅更新非空参考字段");
        Check(((TextBox)pipeInputs["PN"]).Text == string.Empty, "空参考不得覆盖目标字段");
        properties.Close();

        var faceCenter = new PipePoint3D(23, 0, 0);
        var faceNormal = new PipeDirection3D(1, 0, 0);
        var faceAxis = new PipeDirection3D(-1, 0, 0);
        PipelineCircularBoundary Circle(PipePoint3D center, PipeDirection3D axis, double radius, double lengthFactor = 1) =>
            new PipelineCircularBoundary(center, axis, radius, 2 * Math.PI * radius * lengthFactor);
        var annulus = PipelineAnnularFaceGeometry.Create(faceNormal,
            new[] { Circle(faceCenter, faceAxis, 35.5), Circle(faceCenter, faceAxis, 32.5) });
        Check(annulus.Center == faceCenter && annulus.InnerRadiusMm == 32.5 && annulus.OuterRadiusMm == 35.5,
            "蝶阀候选端面中心和半径不正确");
        var rotated = PipelineAnnularFaceGeometry.Create(new PipeDirection3D(0, 0, -3),
            new[] { Circle(new PipePoint3D(5, 6, 7), new PipeDirection3D(0, 0, 2), 25),
                Circle(new PipePoint3D(5, 6, 7), new PipeDirection3D(0, 0, -1), 30) });
        Check(rotated.Center == new PipePoint3D(5, 6, 7) && rotated.Normal.Z == -1, "平移及旋转端面识别失败");
        Reject(() => PipelineAnnularFaceGeometry.Create(faceNormal, new[] { Circle(faceCenter, faceAxis, 32.5) }), "缺少圆边必须拒绝");
        Reject(() => PipelineAnnularFaceGeometry.Create(faceNormal, new[] { Circle(faceCenter, faceAxis, 32.5),
            Circle(faceCenter, faceAxis, 35.5, 0.5) }), "圆弧不能当成完整圆");
        Reject(() => PipelineAnnularFaceGeometry.Create(faceNormal, new[] { Circle(faceCenter, faceAxis, 32.5),
            Circle(new PipePoint3D(23, 0.1, 0), faceAxis, 35.5) }), "不同心圆边必须拒绝");
        Reject(() => PipelineAnnularFaceGeometry.Create(faceNormal, new[] { Circle(faceCenter, faceAxis, 32.5),
            Circle(faceCenter, new PipeDirection3D(0, 1, 0), 35.5) }), "圆边方向错误必须拒绝");
        Reject(() => PipelineAnnularFaceGeometry.Create(faceNormal, new[] { Circle(faceCenter, faceAxis, 32.5),
            Circle(faceCenter, faceAxis, 32.5) }), "重合半径必须拒绝");
        Reject(() => PipelineAnnularFaceGeometry.Create(new PipeDirection3D(0, 0, 0),
            new[] { Circle(faceCenter, faceAxis, 32.5), Circle(faceCenter, faceAxis, 35.5) }), "零法向必须拒绝");
        Reject(() => PipelineAnnularFaceGeometry.Create(faceNormal, new[] { Circle(faceCenter, faceAxis, double.NaN),
            Circle(faceCenter, faceAxis, 35.5) }), "非有限半径必须拒绝");
        _count += FaceComRegression.Run();
        _count += AssemblyFaceContextRegression.Run();
        if (args.Length != 0) _count += PauseSnapshotRegression.Run(args[0]);
        Console.WriteLine("端点和手动坐标回归通过：" + _count + " 项。");
    }
}

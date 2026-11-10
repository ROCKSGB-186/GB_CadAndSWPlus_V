using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;

internal static class EndpointSourceRegression
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _count;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _count++;
    }

    private static T Field<T>(PipelinePropertiesWindow window, string name) =>
        (T)typeof(PipelinePropertiesWindow).GetField(name, PrivateInstance)!.GetValue(window)!;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
            if (child is DependencyObject node)
            {
                yield return node;
                foreach (var descendant in Descendants(node)) yield return descendant;
            }
    }

    // 必须以真正的模态窗口确认，才能验证 DialogResult 和确认快照。
    private static void Confirm(PipelinePropertiesWindow window)
    {
        Exception? error = null;
        window.Loaded += (sender, args) => window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                typeof(PipelinePropertiesWindow).GetMethod("ConfirmClick", PrivateInstance)!
                    .Invoke(window, new object[] { window, new RoutedEventArgs() });
                if (window.DialogResult != true)
                    throw new InvalidOperationException("有效属性未通过确认。");
            }
            catch (Exception ex)
            {
                error = ex;
                window.Close();
            }
        }));
        bool? result = window.ShowDialog();
        if (error != null) throw new InvalidOperationException("确认窗口失败。", error);
        Check(result == true, "确认窗口应返回成功");
    }

    public static int Run()
    {
        _count = 0;
        var start = new Dictionary<string, string> { ["DN"] = "DN100", ["PN"] = "PN16" };
        var end = new Dictionary<string, string> { ["DN"] = "DN80", ["MEDIUM"] = "水" };
        var startSources = new Dictionary<string, string> { ["dn"] = "起点构件 DN 来源", ["PN"] = "起点构件 PN 来源" };
        var endSources = new Dictionary<string, string> { ["DN"] = "终点构件 DN 来源", ["MEDIUM"] = "终点构件介质来源", ["PN"] = "空值不应显示的来源" };
        var saved = new PipelineEndpointReferences(start, end, startSources, endSources);
        start["DN"] = "外部修改";
        end["DN"] = "外部修改";
        startSources["dn"] = "外部修改";
        endSources.Clear();
        Check(saved.Start["dn"] == "DN100" && saved.End["dn"] == "DN80", "端点值应为独立且不区分大小写的快照");
        Check(saved.StartSources["DN"] == "起点构件 DN 来源" && saved.EndSources["dn"] == "终点构件 DN 来源",
            "来源应独立复制且不区分大小写");
        var legacy = new PipelineEndpointReferences(start, end);
        Check(legacy.StartSources.Count == 0 && legacy.EndSources.Count == 0, "旧构造方式应生成空来源字典");
        var explicitNull = new PipelineEndpointReferences(start, end, null, null);
        Check(explicitNull.StartSources.Count == 0 && explicitNull.EndSources.Count == 0, "空来源参数应兼容");

        var selection = new PipelineRouteSelectionSession();
        PipeRoute? route = null;
        Check(selection.TryAddPoint(new PipePoint3D(0, 0, 0), out _) &&
              selection.TryAddPoint(new PipePoint3D(100, 0, 0), out _) &&
              selection.TryComplete(out route, out _) && route != null, "准备有效自由端路线失败");
        // 传入合法管径和壁厚，确保确认路径不会弹出校验提示。
        var attributes = new Dictionary<string, string> { ["PIPE_OD"] = "100", ["PIPE_THK"] = "5", ["DN"] = "DN50" };
        string prompt = string.Empty;
        var window = new PipelinePropertiesWindow(null, attributes, null, true, route: route, endpointReferences: saved,
            confirmEndpointReferences: (message, title) => { prompt = message; return true; });
        var startInputs = Field<Dictionary<string, TextBox>>(window, "_startReferences");
        var endInputs = Field<Dictionary<string, TextBox>>(window, "_endReferences");
        var startLabels = Field<Dictionary<string, TextBlock>>(window, "_startSourceLabels");
        var endLabels = Field<Dictionary<string, TextBlock>>(window, "_endSourceLabels");
        var startMap = Field<Dictionary<string, string>>(window, "_startReferenceSources");
        var endMap = Field<Dictionary<string, string>>(window, "_endReferenceSources");
        Check(startLabels["DN"].Text == "起点构件 DN 来源" && endLabels["DN"].Text == "终点构件 DN 来源",
            "初始化应在两端显示保存的来源");
        Check(!endMap.ContainsKey("PN") && endLabels["PN"].Text == "来源：手工输入或未填写", "空值不得恢复过期来源");
        int startButtons = 0, endButtons = 0;
        foreach (var node in Descendants(window))
            if (node is Button button)
            {
                if (Equals(button.Content, "查询数据库构件参考（起点）")) startButtons++;
                if (Equals(button.Content, "查询数据库构件参考（终点）")) endButtons++;
            }
        Check(startButtons == 1 && endButtons == 1, "两端应各有一个数据库查询按钮");
        startInputs["DN"].Text = "DN125";
        Check(!startMap.ContainsKey("DN") && startLabels["DN"].Text == "来源：手工输入或已修改", "手工修改应清除该字段来源");
        Check(startMap["PN"] == "起点构件 PN 来源" && startLabels["PN"].Text == "起点构件 PN 来源" &&
              endMap["DN"] == "终点构件 DN 来源" && endLabels["DN"].Text == "终点构件 DN 来源",
            "修改起点 DN 不得清除其他字段或终点来源");
        var apply = typeof(PipelinePropertiesWindow).GetMethod("ApplyEndpointReferences", PrivateInstance)!;
        apply.Invoke(window, new object[] { "起点", startInputs, endInputs, startMap });
        Check(prompt.Contains("DN50 → DN125") && prompt.Contains("来源：手工输入或已修改") &&
              prompt.Contains("起点构件 PN 来源") && prompt.Contains("DN80（不一致）"), "采用确认应显示手工及数据库来源和冲突");
        apply.Invoke(window, new object[] { "终点", endInputs, startInputs, endMap });
        Check(prompt.Contains("终点构件 DN 来源") && prompt.Contains("终点构件介质来源"), "采用终点确认应显示终点来源");
        Check(window.ConfirmedEndpointReferences == null, "确认窗口前不应产生快照");
        Confirm(window);
        var confirmed = window.ConfirmedEndpointReferences!;
        Check(confirmed.Start["DN"] == "DN125" && confirmed.End["DN"] == "DN80" &&
              !confirmed.StartSources.ContainsKey("DN") && confirmed.StartSources["PN"] == "起点构件 PN 来源" &&
              confirmed.EndSources["DN"] == "终点构件 DN 来源" && !confirmed.EndSources.ContainsKey("PN"),
            "确认应保存两端值和各自有效来源");
        startMap.Clear(); endMap.Clear(); startInputs["DN"].Text = "确认后修改";
        Check(confirmed.Start["DN"] == "DN125" && confirmed.StartSources["PN"] == "起点构件 PN 来源" &&
              confirmed.EndSources["DN"] == "终点构件 DN 来源", "确认后的来源和值必须独立于窗口状态");

        // 两端相同和不同的非空参考均显示另一端来源，不推断单位或兼容性。
        var comparison = new PipelineEndpointReferences(
            new Dictionary<string, string> { ["DN"] = "100", ["PN"] = "16", ["MEDIUM"] = "水" },
            new Dictionary<string, string> { ["DN"] = "100", ["PN"] = "10" },
            new Dictionary<string, string> { ["DN"] = "起点 DN（单位：mm）", ["PN"] = "起点 PN（单位：MPa）" },
            new Dictionary<string, string> { ["DN"] = "终点 DN（单位：in）" });
        string comparisonPrompt = string.Empty;
        var comparisonWindow = new PipelinePropertiesWindow(null, attributes, null, true, route: route,
            endpointReferences: comparison, confirmEndpointReferences: (message, title) => { comparisonPrompt = message; return false; });
        var comparisonStart = Field<Dictionary<string, TextBox>>(comparisonWindow, "_startReferences");
        var comparisonEnd = Field<Dictionary<string, TextBox>>(comparisonWindow, "_endReferences");
        var comparisonStartSources = Field<Dictionary<string, string>>(comparisonWindow, "_startReferenceSources");
        var comparisonEndSources = Field<Dictionary<string, string>>(comparisonWindow, "_endReferenceSources");
        apply.Invoke(comparisonWindow, new object[] { "起点", comparisonStart, comparisonEnd, comparisonStartSources });
        Check(comparisonPrompt.Contains("系统不转换或比较单位") && comparisonPrompt.Contains("相同数值也不能证明两端兼容"),
            "采用提示必须明确不转换或比较单位，且同值不等于兼容");
        Check(comparisonPrompt.Contains("起点 DN（单位：mm）\n另一端：100；终点 DN（单位：in）") &&
              !comparisonPrompt.Contains("另一端：100（不一致）"),
            "两端同值时仍应显示另一端数据库来源及原单位");
        Check(comparisonPrompt.Contains("起点 PN（单位：MPa）\n另一端：10（不一致）；来源：手工输入或已修改"),
            "另一端不同值且无数据库来源时应显示冲突与手工来源");
        apply.Invoke(comparisonWindow, new object[] { "终点", comparisonEnd, comparisonStart, comparisonEndSources });
        Check(comparisonPrompt.Contains("终点 DN（单位：in）\n另一端：100；起点 DN（单位：mm）") &&
              comparisonPrompt.Contains("来源：手工输入或已修改\n另一端：16（不一致）；起点 PN（单位：MPa）"),
            "反向采用时也应显示同值和冲突值的另一端来源");
        comparisonWindow.Close();

        // 未提供来源时，手工输入和两端留空仍可确认且不会产生数据库来源。
        var manualWindow = new PipelinePropertiesWindow(null, attributes, null, true, route: route);
        var manualInputs = Field<Dictionary<string, TextBox>>(manualWindow, "_startReferences");
        manualInputs["MEDIUM"].Text = "空气";
        Confirm(manualWindow);
        var manual = manualWindow.ConfirmedEndpointReferences!;
        Check(manual.Start["MEDIUM"] == "空气" && manual.End["DN"] == string.Empty &&
              manual.StartSources.Count == 0 && manual.EndSources.Count == 0, "手工参考及空终点应可确认且无来源");
        var emptyWindow = new PipelinePropertiesWindow(null, attributes, null, true, route: route);
        Confirm(emptyWindow);
        var empty = emptyWindow.ConfirmedEndpointReferences!;
        Check(empty.Start["DN"] == string.Empty && empty.End["DN"] == string.Empty &&
              empty.StartSources.Count == 0 && empty.EndSources.Count == 0, "两端留空应可确认且无来源");
        return _count;
    }
}

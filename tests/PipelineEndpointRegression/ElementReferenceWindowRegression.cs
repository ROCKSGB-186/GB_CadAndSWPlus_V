using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using Api = GB_CadAndSWPlus_V.SolidWorksAddIn.Services.SolidWorksResourceApiService;

internal static class ElementReferenceWindowRegression
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _count;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _count++;
    }

    private static T Field<T>(PipelineElementReferenceWindow window, string name) =>
        (T)typeof(PipelineElementReferenceWindow).GetField(name, PrivateInstance)!.GetValue(window)!;

    private static Task Search(PipelineElementReferenceWindow window) =>
        (Task)typeof(PipelineElementReferenceWindow).GetMethod("SearchAsync", PrivateInstance)!.Invoke(window, null)!;

    private static Api.ElementListResponse Elements() => new Api.ElementListResponse
    {
        Success = true, Elements = new List<Api.ElementDto>
        {
            new Api.ElementDto { Id = 1, ElementCode = "E1", Name = "构件一" },
            new Api.ElementDto { Id = 2, ElementCode = "E2", Name = "构件二" }
        }
    };

    private static Api.PropertyListResponse Properties(long id, string value) => new Api.PropertyListResponse
    {
        Success = true, Data = new List<Api.PropertyValueDto>
        {
            new Api.PropertyValueDto { ElementDefinitionId = id, PropertyCode = "DN", PropertyName = "通径", ValueText = value, Unit = "mm" }
        }
    };

    public static int Run()
    {
        _count = 0;
        var api = new Api();
        bool sessionIsCurrent = true;
        var window = new PipelineElementReferenceWindow(new PipelineElementReferenceSource(api, () => sessionIsCurrent));
        Exception? error = null;
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        watchdog.Tick += (sender, args) => { watchdog.Stop(); error = new TimeoutException("数据库参考窗口回归超时。"); window.Close(); };
        window.Loaded += async (sender, args) =>
        {
            try
            {
                var keyword = Field<TextBox>(window, "_keyword");
                var elements = Field<DataGrid>(window, "_elements");
                var properties = Field<DataGrid>(window, "_properties");
                var dn = Field<ComboBox>(window, "_dn");
                var pn = Field<ComboBox>(window, "_pn");
                var medium = Field<ComboBox>(window, "_medium");
                var next = Field<Button>(window, "_next");
                Check(elements.SelectedItem == null && !next.IsEnabled && window.SelectedCandidates == null,
                    "初始状态不得自动选中构件或允许继续");
                var nextClick = typeof(PipelineElementReferenceWindow).GetMethod("NextClick", PrivateInstance)!;
                nextClick.Invoke(window, new object[] { next, new RoutedEventArgs() });
                Check(window.SelectedElement == null && window.SelectedCandidates == null,
                    "未选择构件及属性时即使直接触发继续也不得产生结果");

                // 替身故意忽略取消，验证窗口自身的版本检查可丢弃旧结果。
                var oldSearch = new TaskCompletionSource<Api.ElementListResponse>();
                CancellationToken oldSearchToken = default;
                api.Search = (text, token) => { oldSearchToken = token; return oldSearch.Task; };
                keyword.Text = "旧查询";
                Task pendingSearch = Search(window);
                keyword.Text = "新查询";
                Check(oldSearchToken.IsCancellationRequested && elements.ItemsSource == null && !next.IsEnabled,
                    "修改查询应立即取消并清空旧结果");
                api.Search = (text, token) => Task.FromResult(Elements());
                await Search(window);
                Check(elements.Items.Count == 2 && elements.SelectedItem == null && !next.IsEnabled,
                    "搜索成功后不得默认选择构件");
                var currentSearchResults = elements.ItemsSource;
                oldSearch.SetResult(Elements());
                await pendingSearch;
                Check(ReferenceEquals(elements.ItemsSource, currentSearchResults) && elements.SelectedItem == null,
                    "被取消的旧查询结果不得覆盖新查询");
                var oldProperties = new TaskCompletionSource<Api.PropertyListResponse>();
                var newProperties = new TaskCompletionSource<Api.PropertyListResponse>();
                CancellationToken oldPropertiesToken = default;
                api.List = (id, token) =>
                {
                    if (id == 1) { oldPropertiesToken = token; return oldProperties.Task; }
                    return newProperties.Task;
                };
                elements.SelectedIndex = 0;
                elements.SelectedIndex = 1;
                Check(oldPropertiesToken.IsCancellationRequested && properties.ItemsSource == null && !next.IsEnabled,
                    "切换构件应取消旧属性请求并清空候选");
                newProperties.SetResult(Properties(2, "DN200"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(properties.Items.Count == 1 && ((PipelineReferenceCandidate)properties.Items[0]).Value == "DN200",
                    "新构件属性应正常显示");
                Check(dn.SelectedIndex == 0 && pn.SelectedIndex == 0 && medium.SelectedIndex == 0 && !next.IsEnabled,
                    "所有映射默认不选择，不能继续");
                oldProperties.SetResult(Properties(1, "DN100"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(properties.Items.Count == 1 && ((PipelineReferenceCandidate)properties.Items[0]).Value == "DN200" && !next.IsEnabled,
                    "旧构件属性迟到时不得覆盖新构件");
                dn.SelectedIndex = 1;
                Check(next.IsEnabled, "明确选择属性后才允许继续");
                dn.SelectedIndex = 0;
                Check(!next.IsEnabled, "取消全部属性选择后应再次禁止继续");
                nextClick.Invoke(window, new object[] { next, new RoutedEventArgs() });
                Check(window.SelectedElement == null && window.SelectedCandidates == null,
                    "仅选择构件而未选择属性不得产生结果");

                var editedProperties = new TaskCompletionSource<Api.PropertyListResponse>();
                CancellationToken editedToken = default;
                api.List = (id, token) => { editedToken = token; return editedProperties.Task; };
                elements.SelectedIndex = 0;
                keyword.Text = "修改条件";
                Check(editedToken.IsCancellationRequested && elements.ItemsSource == null && properties.ItemsSource == null && !next.IsEnabled,
                    "修改查询时应同时清除选中构件及属性");
                editedProperties.SetResult(Properties(1, "迟到属性"));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(elements.ItemsSource == null && properties.ItemsSource == null && !next.IsEnabled,
                    "查询修改前的属性结果不得回填");

                api.List = (id, token) => Task.FromResult(Properties(id, "DN200"));
                await Search(window);
                elements.SelectedIndex = 1;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Check(!next.IsEnabled && dn.SelectedIndex == 0, "重新查询后仍需手动选择属性");
                dn.SelectedIndex = 1;
                sessionIsCurrent = false;
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.SelectedElement == null && window.SelectedCandidates == null &&
                      elements.ItemsSource == null && properties.ItemsSource == null && !next.IsEnabled &&
                      Field<TextBlock>(window, "_searchStatus").Text.Contains("会话已失效或切换"),
                    "确认时会话失效应阻断提交并清除查询结果");
                sessionIsCurrent = true;
                await Search(window);
                elements.SelectedIndex = 1;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                dn.SelectedIndex = 1;
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex)
            {
                error = ex;
                window.Close();
            }
        };
        watchdog.Start();
        bool? result = window.ShowDialog();
        watchdog.Stop();
        if (error != null) throw new InvalidOperationException("数据库参考窗口回归失败。", error);
        Check(result == true && window.SelectedElement?.Id == 2 && window.SelectedCandidates != null &&
              window.SelectedCandidates.Count == 1 && window.SelectedCandidates["DN"].Value == "DN200" &&
              window.SelectedCandidates["DN"].Unit == "mm", "明确选择后继续应返回指定构件及属性");
        return _count;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using Newtonsoft.Json;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>手工查询统一构件定义，并为本次端点选择参考属性。</summary>
    internal sealed class PipelineElementReferenceWindow : Window
    {
        private readonly PipelineElementReferenceSource _source;
        private readonly TextBox _keyword;
        private readonly DataGrid _elements;
        private readonly DataGrid _properties;
        private readonly ComboBox _dn;
        private readonly ComboBox _pn;
        private readonly ComboBox _medium;
        private readonly Button _retryProperties;
        private readonly Button _next;
        private readonly TextBlock _searchStatus;
        private readonly TextBlock _propertyStatus;
        private CancellationTokenSource? _searchCancellation;
        private CancellationTokenSource? _propertyCancellation;
        private long _searchVersion;
        private long _propertyVersion;
        private bool _closed;
        private bool _resettingElements;

        public PipelineElementReferenceWindow(PipelineElementReferenceSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Title = "选择统一构件参考属性";
            Width = 900;
            Height = 720;
            MinWidth = 720;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddRow(root, new TextBlock
            {
                Text = "手工选择的统一构件定义，仅供本次参考；不是设备实例或管口关联，数据库不会被修改；属性及单位需人工核对。",
                Foreground = Brushes.DarkOrange,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            }, 0);

            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var searchButton = new Button { Content = "查询", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(searchButton, Dock.Right);
            searchRow.Children.Add(searchButton);
            var searchLabel = new TextBlock { Text = "名称或编码：", VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(searchLabel, Dock.Left);
            searchRow.Children.Add(searchLabel);
            _keyword = new TextBox { MinWidth = 160, VerticalContentAlignment = VerticalAlignment.Center };
            searchRow.Children.Add(_keyword);
            AddRow(root, searchRow, 1);

            _searchStatus = new TextBlock { Text = "请输入至少 2 个字符后查询。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            AddRow(root, _searchStatus, 2);
            _elements = CreateGrid();
            AddColumn(_elements, "名称", "Name");
            AddColumn(_elements, "编码", "Code");
            AddColumn(_elements, "ID", "Id");
            AddColumn(_elements, "状态", "Status");
            AddRow(root, _elements, 3);

            var propertyHeading = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
            _retryProperties = new Button { Content = "重试读取属性", Padding = new Thickness(8, 2, 8, 2), IsEnabled = false };
            DockPanel.SetDock(_retryProperties, Dock.Right);
            propertyHeading.Children.Add(_retryProperties);
            _propertyStatus = new TextBlock { Text = "请选择一个统一构件定义。", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            propertyHeading.Children.Add(_propertyStatus);
            AddRow(root, propertyHeading, 4);
            _properties = CreateGrid();
            AddColumn(_properties, "编码", "Code");
            AddColumn(_properties, "名称", "Name");
            AddColumn(_properties, "原单位", "Unit");
            AddColumn(_properties, "值", "Value");
            AddRow(root, _properties, 5);

            var mappings = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            mappings.Children.Add(new TextBlock { Text = "手动选择参考属性（保留原单位，不自动推断）：", TextWrapping = TextWrapping.Wrap });
            _dn = AddMapping(mappings, "DN");
            _pn = AddMapping(mappings, "PN");
            _medium = AddMapping(mappings, "介质");
            AddRow(root, mappings, 6);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _next = new Button { Content = "下一步", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(4), IsEnabled = false };
            var cancel = new Button { Content = "取消", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(4), IsCancel = true };
            buttons.Children.Add(_next);
            buttons.Children.Add(cancel);
            AddRow(root, buttons, 7);
            Content = root;

            // 输入变化立即使旧查询和属性失效，即使服务忽略取消也不能回填。
            _keyword.TextChanged += (sender, args) => ResetSearch("请输入至少 2 个字符后查询。");
            searchButton.Click += async (sender, args) => await SearchAsync();
            _elements.SelectionChanged += async (sender, args) =>
            {
                if (!_resettingElements) await LoadPropertiesAsync();
            };
            _retryProperties.Click += async (sender, args) => await LoadPropertiesAsync();
            _dn.SelectionChanged += MappingChanged;
            _pn.SelectionChanged += MappingChanged;
            _medium.SelectionChanged += MappingChanged;
            _next.Click += NextClick;
            Closed += (sender, args) =>
            {
                _closed = true;
                CancelSearch();
                CancelProperties();
                SolidWorksFileLogger.Info("统一构件参考窗口已关闭；未修改数据库。");
            };
            SolidWorksFileLogger.Info("打开统一构件参考窗口；仅供手工选择本次参考。");
        }

        public PipelineReferenceElement? SelectedElement { get; private set; }
        public IReadOnlyDictionary<string, PipelineReferenceCandidate>? SelectedCandidates { get; private set; }

        private async System.Threading.Tasks.Task SearchAsync()
        {
            string keyword = _keyword.Text.Trim();
            ResetSearch("正在查询统一构件定义……");
            if (keyword.Length < 2 || keyword.Length > 100)
            {
                _searchStatus.Text = "请输入 2 至 100 个字符后查询。";
                SolidWorksFileLogger.Warning("统一构件查询未执行：查询条件长度无效。");
                return;
            }

            long version = _searchVersion;
            var cancellation = new CancellationTokenSource();
            _searchCancellation = cancellation;
            SolidWorksFileLogger.Info("开始查询统一构件定义。");
            try
            {
                var results = await _source.SearchAsync(keyword, cancellation.Token);
                if (!IsCurrentSearch(version, cancellation)) return;
                _elements.ItemsSource = results;
                _searchStatus.Text = results.Count == 0 ? "未查询到统一构件定义，请修改条件后重试。" : "请选择一个统一构件定义。";
                SolidWorksFileLogger.Info("统一构件查询完成；结果数=" + results.Count + "。");
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentSearch(version, cancellation)) _searchStatus.Text = "查询已取消，可重新查询。";
            }
            catch (Exception ex) when (IsExpectedError(ex))
            {
                if (!IsCurrentSearch(version, cancellation)) return;
                _searchStatus.Text = ex is PipelineReferenceException referenceError
                    ? referenceError.Message : "查询失败，请检查连接后重试，或取消。";
                SolidWorksFileLogger.Warning("统一构件查询失败；异常类型=" + ex.GetType().Name + "。");
            }
            finally
            {
                if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null;
                cancellation.Dispose();
            }
        }

        private async System.Threading.Tasks.Task LoadPropertiesAsync()
        {
            ResetProperties();
            var element = _elements.SelectedItem as PipelineReferenceElement;
            if (element == null)
            {
                _propertyStatus.Text = "请选择一个统一构件定义。";
                return;
            }

            long version = _propertyVersion;
            var cancellation = new CancellationTokenSource();
            _propertyCancellation = cancellation;
            _propertyStatus.Text = "正在读取所选统一构件的属性……";
            SolidWorksFileLogger.Info("开始读取所选统一构件的参考属性。");
            try
            {
                var properties = await _source.GetPropertiesAsync(element.Id, cancellation.Token);
                if (!IsCurrentProperties(version, cancellation, element)) return;
                _properties.ItemsSource = properties;
                var choices = new List<CandidateChoice> { new CandidateChoice(null) };
                choices.AddRange(properties.Select(candidate => new CandidateChoice(candidate)));
                SetChoices(_dn, choices);
                SetChoices(_pn, choices);
                SetChoices(_medium, choices);
                _propertyStatus.Text = properties.Count == 0 ? "该构件没有可用的单值属性，可重试或选择其他构件。" :
                    "请逐项手动选择属性，并核对值及原单位。空值、布尔值、多值及含控制字符的属性未列出。";
                _retryProperties.IsEnabled = true;
                SolidWorksFileLogger.Info("统一构件参考属性读取完成；候选数=" + properties.Count + "。");
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentProperties(version, cancellation, element))
                {
                    _propertyStatus.Text = "属性读取已取消，可重试或选择其他构件。";
                    _retryProperties.IsEnabled = true;
                }
            }
            catch (Exception ex) when (IsExpectedError(ex))
            {
                if (!IsCurrentProperties(version, cancellation, element)) return;
                _propertyStatus.Text = ex is PipelineReferenceException referenceError
                    ? referenceError.Message : "属性读取失败，请重试、选择其他构件或取消。";
                _retryProperties.IsEnabled = true;
                SolidWorksFileLogger.Warning("统一构件参考属性读取失败；异常类型=" + ex.GetType().Name + "。");
            }
            finally
            {
                if (ReferenceEquals(_propertyCancellation, cancellation)) _propertyCancellation = null;
                cancellation.Dispose();
            }
        }

        private void ResetSearch(string message)
        {
            CancelSearch();
            _resettingElements = true;
            try
            {
                _elements.SelectedItem = null;
                _elements.ItemsSource = null;
            }
            finally { _resettingElements = false; }
            ResetProperties();
            _searchStatus.Text = message;
            _propertyStatus.Text = "请选择一个统一构件定义。";
        }

        private void ResetProperties()
        {
            CancelProperties();
            _properties.ItemsSource = null;
            _dn.ItemsSource = null;
            _pn.ItemsSource = null;
            _medium.ItemsSource = null;
            _retryProperties.IsEnabled = false;
            _next.IsEnabled = false;
        }

        private void CancelSearch()
        {
            ++_searchVersion;
            _searchCancellation?.Cancel();
            _searchCancellation = null;
        }

        private void CancelProperties()
        {
            ++_propertyVersion;
            _propertyCancellation?.Cancel();
            _propertyCancellation = null;
        }

        private bool IsCurrentSearch(long version, CancellationTokenSource cancellation) =>
            !_closed && version == _searchVersion && !cancellation.IsCancellationRequested;

        private bool IsCurrentProperties(long version, CancellationTokenSource cancellation, PipelineReferenceElement element) =>
            !_closed && version == _propertyVersion && !cancellation.IsCancellationRequested && ReferenceEquals(_elements.SelectedItem, element);

        private static bool IsExpectedError(Exception ex) =>
            ex is IOException || ex is HttpRequestException || ex is InvalidOperationException ||
            ex is ArgumentException || ex is JsonException || ex is TimeoutException;

        private void MappingChanged(object sender, SelectionChangedEventArgs e)
        {
            _next.IsEnabled = _elements.SelectedItem is PipelineReferenceElement &&
                (GetCandidate(_dn) != null || GetCandidate(_pn) != null || GetCandidate(_medium) != null);
        }

        private void NextClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _source.EnsureSessionCurrent();
            }
            catch (PipelineReferenceException ex)
            {
                ResetSearch(ex.Message);
                SolidWorksFileLogger.Warning("未采用数据库构件参考：统一登录会话已失效或切换。");
                return;
            }
            var element = _elements.SelectedItem as PipelineReferenceElement;
            var selected = new Dictionary<string, PipelineReferenceCandidate>();
            AddCandidate(selected, "DN", _dn);
            AddCandidate(selected, "PN", _pn);
            AddCandidate(selected, "MEDIUM", _medium);
            if (element == null || selected.Count == 0)
            {
                _propertyStatus.Text = "请选择构件并至少选择一项参考属性。";
                SolidWorksFileLogger.Warning("未选择统一构件参考属性：构件或属性为空。");
                return;
            }
            SelectedElement = element;
            SelectedCandidates = new ReadOnlyDictionary<string, PipelineReferenceCandidate>(selected);
            SolidWorksFileLogger.Info("已手工选择统一构件参考属性；字段数=" + selected.Count + "；未修改数据库。");
            DialogResult = true;
        }

        private static void AddCandidate(IDictionary<string, PipelineReferenceCandidate> selected, string key, ComboBox box)
        {
            var candidate = GetCandidate(box);
            if (candidate != null) selected.Add(key, candidate);
        }

        private static PipelineReferenceCandidate? GetCandidate(ComboBox box) => (box.SelectedItem as CandidateChoice)?.Candidate;

        private static void SetChoices(ComboBox box, IReadOnlyList<CandidateChoice> choices)
        {
            box.ItemsSource = choices;
            box.SelectedIndex = 0;
        }

        private static ComboBox AddMapping(Panel parent, string label)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var caption = new TextBlock { Text = label + "：", Width = 65, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            var box = new ComboBox { DisplayMemberPath = "Display", MinWidth = 200 };
            row.Children.Add(box);
            parent.Children.Add(row);
            return box;
        }

        private static DataGrid CreateGrid() => new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 70
        };

        private static void AddColumn(DataGrid grid, string header, string path)
        {
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        }

        private static void AddRow(Grid grid, UIElement element, int row)
        {
            Grid.SetRow(element, row);
            grid.Children.Add(element);
        }

        private sealed class CandidateChoice
        {
            public CandidateChoice(PipelineReferenceCandidate? candidate) { Candidate = candidate; }
            public PipelineReferenceCandidate? Candidate { get; }
            public string Display => Candidate == null ? "不选择" :
                Candidate.Name + " [" + Candidate.Code + "]：" + Candidate.Value + "（原单位：" +
                (string.IsNullOrWhiteSpace(Candidate.Unit) ? "未提供" : Candidate.Unit) + "）";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using GB_CadAndSWPlus_V.Shared.Services;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// 独立模态窗口：ShowDialog 返回 true 后使用 ConfirmedAttributes 绘制；
    /// 模型成功后由调用方决定是否调用 SaveAfterModelSuccess()。
    /// 取消不回滚此前用户主动点击的“保存配置”。
    /// </summary>
    public sealed class PipelinePropertiesWindow : Window
    {
        private readonly Dictionary<string, Control> _editors = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _initialValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly TextBlock _status;
        private readonly CheckBox? _autoRebuild;
        private readonly Func<string, string, bool> _confirmEndpointReferences;
        private readonly Dictionary<string, TextBox> _startReferences = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBox> _endReferences = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _startReferenceSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _endReferenceSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBlock> _startSourceLabels = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBlock> _endSourceLabels = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        private bool _loadFailed;

        public IReadOnlyList<PipelinePropertiesFieldDefinition> Fields { get; }
        public PipelinePropertiesConfigurationService ConfigurationService { get; }
        /// <summary>仅表示窗口初始化时的读取结果；文件不存在视为正常首次运行。</summary>
        public bool ConfigurationLoadSucceeded { get; private set; }

        /// <summary>仅在确定并完成校验后产生，取消时为 null；调用方应在建模成功后再保存。</summary>
        public IDictionary<string, string> ConfirmedAttributes { get; private set; }
        internal bool AutoRebuildOnInsert => _autoRebuild?.IsChecked == true;
        internal PipelineEndpointReferences? ConfirmedEndpointReferences { get; private set; }

        /// <param name="fields">服务端目录转换后的字段；null 或空集合使用本地精简字段，不使用目录默认值。</param>
        /// <param name="initialAttributes">本次实例属性，优先于本地最后属性；起终点可为空。</param>
        /// <param name="configurationService">可选本地配置服务。</param>
        public PipelinePropertiesWindow(
            IEnumerable<PipelinePropertiesFieldDefinition> fields = null,
            IDictionary<string, string> initialAttributes = null,
            PipelinePropertiesConfigurationService configurationService = null)
            : this(fields, initialAttributes, configurationService, false)
        {
        }

        internal PipelinePropertiesWindow(
            IEnumerable<PipelinePropertiesFieldDefinition>? fields,
            IDictionary<string, string>? initialAttributes,
            PipelinePropertiesConfigurationService? configurationService,
            bool isTrial,
            bool startAssemblyTrial = false,
            bool autoRebuild = true,
            string? assemblyTrialBlockReason = null,
            PipeRoute? route = null,
            PipelineEndpointReferences? endpointReferences = null,
            Func<string, string, bool>? confirmEndpointReferences = null)
        {
            SolidWorksFileLogger.Start("打开绘管属性窗口");
            _confirmEndpointReferences = confirmEndpointReferences ?? ((message, title) =>
                MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) == MessageBoxResult.Yes);
            ConfigurationService = configurationService ?? new PipelinePropertiesConfigurationService();
            var supplied = fields == null ? new List<PipelinePropertiesFieldDefinition>() : fields.ToList();
            bool offline = supplied.Count == 0;
            var definitions = new List<PipelinePropertiesFieldDefinition>();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in supplied)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Tag) || field.Tag != field.Tag.Trim() || !tags.Add(field.Tag))
                    throw new ArgumentException("管道字段目录包含空字段、无效 Tag 或重复 Tag。", nameof(fields));
                definitions.Add(field);
            }

            // 即使服务端目录不完整，也要提供模型尺寸及必要的本地核心输入。
            var added = new List<string>();
            foreach (var field in PipelinePropertiesCoreFields.Create())
                if (tags.Add(field.Tag))
                {
                    definitions.Add(field);
                    added.Add(field.Tag);
                }
            Fields = new ReadOnlyCollection<PipelinePropertiesFieldDefinition>(definitions
                .OrderBy(field => field.SortOrder).ThenBy(field => field.Tag, StringComparer.OrdinalIgnoreCase).ToList());
            if (initialAttributes != null)
                foreach (var pair in initialAttributes)
                    _initialValues[pair.Key] = pair.Value ?? string.Empty;

            IDictionary<string, string> lastValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                lastValues = ConfigurationService.Load(Fields);
                ConfigurationLoadSucceeded = true;
            }
            catch (Exception ex)
            {
                _loadFailed = true;
                SolidWorksFileLogger.Error("绘管属性窗口加载本地配置失败；异常类型=" + ex.GetType().Name + "。");
            }

            Title = isTrial ? "SolidWorks 绘管属性——试运行" : "SolidWorks 绘管属性";
            Width = 620;
            Height = 680;
            MinWidth = 480;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(14) };
            string catalogNotice = offline
                ? "服务器目录不可用或未提供，当前使用本地精简字段（DN、PN、材质、介质、外径、壁厚、壁厚等级）。"
                : added.Count > 0 ? "服务端目录缺少部分核心字段，已使用本地精简字段补充：" + string.Join("、", added) + "。"
                : "当前使用调用方提供的服务器字段目录。";
            _status = new TextBlock
            {
                Text = (isTrial
                    ? (startAssemblyTrial
                        ? "当前为试运行：点击“确定”后将检查空装配体，并要求选择新文件及确认，才会生成并插入直管；不会自动保存装配体或属性配置。\n"
                        : "当前为属性试运行：此路线暂不能自动插入；点击“确定”只确认属性，不生成模型或自动保存配置。" +
                          (string.IsNullOrWhiteSpace(assemblyTrialBlockReason) ? "" : "\n原因：" + assemblyTrialBlockReason) + "\n")
                    : string.Empty) +
                    catalogNotice + "\n属性和尺寸尚未进行工程规范校核；自由起终点无需连接设备。" +
                    (_loadFailed ? "\n本地配置加载失败，未使用已保存属性；请检查配置文件，勿假定读取成功。" : string.Empty),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DarkOrange,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(_status, Dock.Top);
            root.Children.Add(_status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "保存配置", MinWidth = 88, Margin = new Thickness(4) };
            save.Click += SaveClick;
            var ok = new Button { Content = "确定", MinWidth = 78, Margin = new Thickness(4), IsDefault = true };
            ok.Click += ConfirmClick;
            var cancel = new Button { Content = "取消", MinWidth = 78, Margin = new Thickness(4), IsCancel = true };
            cancel.Click += (sender, args) => SolidWorksFileLogger.Info("绘管属性窗口取消，未保存当前更改。");
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            if (startAssemblyTrial)
            {
                _autoRebuild = new CheckBox
                {
                    Content = "插入后自动重建空装配体（不自动保存）",
                    IsChecked = autoRebuild,
                    Margin = new Thickness(4, 4, 4, 8)
                };
                DockPanel.SetDock(_autoRebuild, Dock.Bottom);
                root.Children.Add(_autoRebuild);
            }

            var panel = new StackPanel();
            if (route != null)
                panel.Children.Add(CreateEndpointReferencePanel(route, endpointReferences));
            foreach (var group in Fields.GroupBy(field => string.IsNullOrWhiteSpace(field.Group) ? "其他" : field.Group))
            {
                var groupBox = new GroupBox { Header = group.Key, Margin = new Thickness(0, 0, 0, 8) };
                var grid = new Grid { Margin = new Thickness(8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                foreach (var field in group)
                {
                    int row = grid.RowDefinitions.Count;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    bool requiredInput = field.Editable && !IsFreeEndpoint(field.Tag) &&
                        (field.Required || string.Equals(field.Tag, "PIPE_OD", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(field.Tag, "PIPE_THK", StringComparison.OrdinalIgnoreCase));
                    var label = new TextBlock
                    {
                        Text = (requiredInput ? "＊ " : "") +
                            (string.IsNullOrWhiteSpace(field.Label) ? field.Tag : field.Label) + "：",
                        Foreground = requiredInput ? Brushes.Firebrick : Brushes.Black,
                        FontWeight = requiredInput ? FontWeights.Bold : FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 8, 4)
                    };
                    Grid.SetRow(label, row);
                    grid.Children.Add(label);
                    string value;
                    if (!_initialValues.TryGetValue(field.Tag, out value) && !lastValues.TryGetValue(field.Tag, out value))
                        value = string.Empty;
                    Control editor;
                    if (field.Editable && field.Options != null && field.Options.Count > 0)
                    {
                        var combo = new ComboBox { IsEditable = true, Margin = new Thickness(0, 4, 0, 4) };
                        foreach (string option in field.Options)
                            if (!string.IsNullOrWhiteSpace(option)) combo.Items.Add(option);
                        combo.Text = value;
                        editor = combo;
                    }
                    else
                    {
                        editor = new TextBox
                        {
                            Text = value, Margin = new Thickness(0, 4, 0, 4),
                            IsReadOnly = !field.Editable,
                            AcceptsReturn = field.Multiline, TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                            MinHeight = field.Multiline ? 50 : 0
                        };
                    }
                    _editors.Add(field.Tag, editor);
                    Grid.SetRow(editor, row);
                    Grid.SetColumn(editor, 1);
                    grid.Children.Add(editor);
                }
                groupBox.Content = grid;
                panel.Children.Add(groupBox);
            }
            root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
            if (offline) SolidWorksFileLogger.Warning("服务器绘管字段目录不可用或未提供，使用本地精简字段。");
            else if (added.Count > 0) SolidWorksFileLogger.Warning("服务器绘管字段目录不完整，已补充本地核心字段。");
            if (_loadFailed)
                Loaded += (sender, args) => MessageBox.Show(this, "本地绘管配置加载失败，未恢复上次属性。请检查配置文件后再保存。",
                    "绘管配置读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            SolidWorksFileLogger.Complete("打开绘管属性窗口");
        }

        /// <summary>供调用方在模型成功创建后调用；读取失败且未由用户明确覆盖时禁止自动保存。</summary>
        public void SaveAfterModelSuccess()
        {
            if (ConfirmedAttributes == null)
                throw new InvalidOperationException("尚未确认绘管属性，不能保存模型配置。");
            if (_loadFailed)
            {
                SolidWorksFileLogger.Warning("绘管配置读取失败且未经用户确认覆盖，跳过模型成功后的自动保存。");
                throw new InvalidOperationException("本地配置读取失败，请先检查配置或在窗口中明确确认覆盖。");
            }
            ConfigurationService.Save(Fields, ConfirmedAttributes);
        }

        private Dictionary<string, string> ReadValues()
        {
            var values = new Dictionary<string, string>(_initialValues, StringComparer.OrdinalIgnoreCase);
            foreach (var item in _editors)
            {
                var combo = item.Value as ComboBox;
                values[item.Key] = (combo != null ? combo.Text : ((TextBox)item.Value).Text).Trim();
            }
            return values;
        }

        private UIElement CreateEndpointReferencePanel(PipeRoute route, PipelineEndpointReferences? references)
        {
            var content = new StackPanel { Margin = new Thickness(8) };
            content.Children.Add(new TextBlock
            {
                Text = "手工输入或手动选择数据库构件定义作为参考，不表示已识别或连接设备管口，也不写入数据库。两端均可留空；请核对参数及单位。",
                Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap
            });
            AddEndpointReferenceGroup(content, "起点", route.Points[0], _startReferences, references?.Start,
                _startReferenceSources, _startSourceLabels, references?.StartSources);
            AddEndpointReferenceGroup(content, "终点", route.Points[route.Points.Count - 1], _endReferences, references?.End,
                _endReferenceSources, _endSourceLabels, references?.EndSources);
            return new Expander
            {
                Header = "起终点参考参数（可选）",
                Content = content,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private void AddEndpointReferenceGroup(Panel parent, string name, PipePoint3D point,
            Dictionary<string, TextBox> editors, IReadOnlyDictionary<string, string>? values,
            Dictionary<string, string> sources, Dictionary<string, TextBlock> sourceLabels,
            IReadOnlyDictionary<string, string>? savedSources)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            panel.Children.Add(new TextBlock
            {
                Text = "路线" + name + "（mm）：" + point.X.ToString("G17", CultureInfo.InvariantCulture) + "，" +
                    point.Y.ToString("G17", CultureInfo.InvariantCulture) + "，" + point.Z.ToString("G17", CultureInfo.InvariantCulture),
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var item in new[] { Tuple.Create("DN", "管口公称通径 DN"), Tuple.Create("PN", "管口公称压力 PN"),
                Tuple.Create("MEDIUM", "适用介质") })
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                var label = new TextBlock { Text = item.Item2 + "：", Width = 165, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
                string value = string.Empty;
                if (values != null && values.TryGetValue(item.Item1, out string saved)) value = saved;
                var input = new TextBox { Text = value };
                editors.Add(item.Item1, input);
                row.Children.Add(input);
                panel.Children.Add(row);
                var sourceLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(165, 0, 0, 0) };
                sourceLabels.Add(item.Item1, sourceLabel);
                if (!string.IsNullOrWhiteSpace(value) && savedSources != null &&
                    savedSources.TryGetValue(item.Item1, out string savedSource) && !string.IsNullOrWhiteSpace(savedSource))
                    sources[item.Item1] = savedSource;
                sourceLabel.Text = sources.TryGetValue(item.Item1, out string origin) ? origin : "来源：手工输入或未填写";
                string tag = item.Item1;
                input.TextChanged += (sender, args) =>
                {
                    sources.Remove(tag);
                    sourceLabels[tag].Text = "来源：手工输入或已修改";
                };
                panel.Children.Add(sourceLabel);
            }
            var choose = new Button { Content = "查询数据库构件参考（" + name + "）", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
            choose.Click += (sender, args) => ChooseDatabaseReferences(name, editors, sources, sourceLabels);
            panel.Children.Add(choose);
            var apply = new Button { Content = "核对并采用" + name + "参考值", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
            apply.Click += (sender, args) => ApplyEndpointReferences(name, editors,
                ReferenceEquals(editors, _startReferences) ? _endReferences : _startReferences, sources);
            panel.Children.Add(apply);
            parent.Children.Add(new GroupBox { Header = name + "参考（可留空）", Content = panel, Margin = new Thickness(0, 6, 0, 0) });
        }

        private void ChooseDatabaseReferences(string name, Dictionary<string, TextBox> editors,
            Dictionary<string, string> sources, Dictionary<string, TextBlock> sourceLabels)
        {
            var session = new SharedLoginSessionStore().Load();
            if (string.IsNullOrWhiteSpace(session.AccessToken) ||
                (session.AccessTokenExpiresAtUtc.HasValue && session.AccessTokenExpiresAtUtc.Value <= DateTime.UtcNow))
            {
                SolidWorksFileLogger.Warning("数据库构件参考查询未启动：统一登录会话不存在或已过期。");
                MessageBox.Show(this, "请先通过统一登录页面登录；离线时仍可手工填写或留空。", "数据库构件参考",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Func<bool> sessionIsCurrent = () =>
            {
                var current = new SharedLoginSessionStore().Load();
                return !string.IsNullOrWhiteSpace(current.AccessToken) &&
                    (!current.AccessTokenExpiresAtUtc.HasValue || current.AccessTokenExpiresAtUtc.Value > DateTime.UtcNow) &&
                    string.Equals(current.AccessToken, session.AccessToken, StringComparison.Ordinal) &&
                    string.Equals(current.Username, session.Username, StringComparison.Ordinal) &&
                    string.Equals(current.ServerHost, session.ServerHost, StringComparison.OrdinalIgnoreCase) &&
                    current.ApiPort == session.ApiPort && current.AccessTokenExpiresAtUtc == session.AccessTokenExpiresAtUtc;
            };
            var source = new PipelineElementReferenceSource(new SolidWorksResourceApiService(session), sessionIsCurrent);
            var window = new PipelineElementReferenceWindow(source) { Owner = this };
            if (window.ShowDialog() != true || window.SelectedElement == null || window.SelectedCandidates == null) return;
            var element = window.SelectedElement;
            var candidates = window.SelectedCandidates;
            var summary = new StringBuilder("仅从用户选择的统一构件定义读取，不是设备实例或管口关联，不写数据库。\n请逐项核对原属性、数值和单位；系统不会转换单位：\n");
            summary.Append("\n构件：").Append(element.Label).Append("\n");
            foreach (var pair in candidates)
            {
                string oldValue = editors[pair.Key].Text.Trim();
                summary.Append("\n").Append(pair.Key).Append("：").Append(oldValue.Length == 0 ? "（空）" : oldValue)
                    .Append(" → ").Append(pair.Value.Value).Append("\n来源字段：").Append(pair.Value.Label).Append("\n");
            }
            summary.Append("\n仅更新").Append(name).Append("参考值；管道属性仍需另行核对并采用。");
            if (!_confirmEndpointReferences(summary.ToString(), "确认更新" + name + "参考值"))
            {
                SolidWorksFileLogger.Info("用户取消从数据库构件更新" + name + "参考值。");
                return;
            }
            if (!sessionIsCurrent())
            {
                SolidWorksFileLogger.Warning("未更新数据库构件参考：确认时统一登录会话已失效或切换。");
                MessageBox.Show(this, "统一登录会话已失效或切换，请重新查询；参考值未更改。", "数据库构件参考",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            foreach (var pair in candidates)
            {
                editors[pair.Key].Text = pair.Value.Value;
                string origin = "来源：数据库统一构件 " + element.Label + "；字段 " + pair.Value.Label + "（本次快照）";
                sources[pair.Key] = origin;
                sourceLabels[pair.Key].Text = origin;
            }
            SolidWorksFileLogger.Info("用户确认从数据库构件更新" + name + "参考；构件ID=" + element.Id +
                "；字段=" + string.Join("、", candidates.Keys) + "；未写入数据库。");
        }

        private void ApplyEndpointReferences(string name, Dictionary<string, TextBox> source,
            Dictionary<string, TextBox> other, Dictionary<string, string> sources)
        {
            var editable = new HashSet<string>(Fields.Where(field => field.Editable).Select(field => field.Tag),
                StringComparer.OrdinalIgnoreCase);
            var updates = PipelineEndpointReferencePlanner.Create(
                ReadEndpointReferences(source), ReadEndpointReferences(other), ReadValues(), editable);
            var summary = new StringBuilder("参考值未经验证，不代表设备关联。系统不转换或比较单位，相同数值也不能证明两端兼容；请核对后确认：\n");
            var otherSources = ReferenceEquals(other, _startReferences) ? _startReferenceSources : _endReferenceSources;
            foreach (var update in updates)
            {
                summary.Append("\n").Append(update.Tag).Append("：").Append(update.Current.Length == 0 ? "（空）" : update.Current)
                    .Append(" → ").Append(update.Value);
                summary.Append("\n").Append(sources.TryGetValue(update.Tag, out string origin) ? origin : "来源：手工输入或已修改");
                if (update.Other.Length != 0)
                {
                    summary.Append("\n另一端：").Append(update.Other);
                    if (update.ConflictsWithOther) summary.Append("（不一致）");
                    summary.Append("；").Append(otherSources.TryGetValue(update.Tag, out string otherOrigin)
                        ? otherOrigin : "来源：手工输入或已修改");
                }
            }
            if (updates.Count == 0)
            {
                SolidWorksFileLogger.Warning("未采用" + name + "参考值：没有可用的参考值或目标字段不可编辑。");
                MessageBox.Show(this, "没有可采用的参考值，或对应管道字段不可编辑。", "端点参考参数",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!_confirmEndpointReferences(summary.ToString(), "确认采用" + name + "参考值"))
            {
                SolidWorksFileLogger.Info("用户取消采用" + name + "参考参数。");
                return;
            }
            foreach (var update in updates)
            {
                Control editor = _editors[update.Tag];
                if (editor is ComboBox combo) combo.Text = update.Value;
                else ((TextBox)editor).Text = update.Value;
            }
            SolidWorksFileLogger.Info("用户确认采用" + name + "参考参数；字段=" + string.Join("、", updates.Select(update => update.Tag)) + "。");
        }

        private static Dictionary<string, string> ReadEndpointReferences(Dictionary<string, TextBox> editors)
        {
            return editors.ToDictionary(pair => pair.Key, pair => pair.Value.Text.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var values = ReadValues();
                try
                {
                    ConfigurationService.Save(Fields, values);
                }
                catch (PipelinePropertiesInvalidConfigurationException)
                {
                    if (MessageBox.Show(this, "原配置已损坏或版本不受支持，覆盖后无法保留原有属性。是否继续？",
                        "确认覆盖配置", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    ConfigurationService.Save(Fields, values, overwriteInvalidConfiguration: true);
                }
                _loadFailed = false;
                _status.Text = _status.Text.Replace("\n本地配置加载失败，未使用已保存属性；请检查配置文件，勿假定读取成功。", string.Empty);
                MessageBox.Show(this, "配置已保存；窗口保持打开。", "绘管配置", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Error("绘管属性窗口保存配置失败；异常类型=" + ex.GetType().Name + "。");
                MessageBox.Show(this, "配置保存失败，原配置未被替换。请检查本地配置、目录及权限。", "绘管配置保存失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ConfirmClick(object sender, RoutedEventArgs e)
        {
            var values = ReadValues();
            foreach (var field in Fields)
            {
                // 自由端是合法状态，不因目录的必填标记而阻止绘制。
                if (field.Editable && field.Required && !IsFreeEndpoint(field.Tag) &&
                    string.IsNullOrWhiteSpace(values[field.Tag]))
                {
                    SolidWorksFileLogger.Warning("绘管属性校验失败：必填字段为空。");
                    MessageBox.Show(this, "请填写：" + (string.IsNullOrWhiteSpace(field.Label) ? field.Tag : field.Label),
                        "绘管属性校验", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _editors[field.Tag].Focus();
                    return;
                }
            }

            double outerDiameter, thickness;
            if (!PipelineStraightParameters.TryPositiveNumber(values["PIPE_OD"], out outerDiameter) ||
                !PipelineStraightParameters.TryPositiveNumber(values["PIPE_THK"], out thickness) || thickness >= outerDiameter / 2)
            {
                SolidWorksFileLogger.Warning("绘管属性校验失败：外径或壁厚无效。");
                MessageBox.Show(this, "请输入有效的正数外径和壁厚（单位：mm），壁厚必须小于外径的一半。",
                    "绘管尺寸校验", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ConfirmedAttributes = values;
            if (_startReferences.Count != 0)
                ConfirmedEndpointReferences = new PipelineEndpointReferences(
                    ReadEndpointReferences(_startReferences), ReadEndpointReferences(_endReferences),
                    _startReferenceSources, _endReferenceSources);
            SolidWorksFileLogger.Info("绘管属性确认通过；尚未保存，等待模型创建成功后由调用方处理。");
            DialogResult = true;
        }

        private static bool IsFreeEndpoint(string tag)
        {
            return string.Equals(tag, "START_POINT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "END_POINT", StringComparison.OrdinalIgnoreCase);
        }
    }
}

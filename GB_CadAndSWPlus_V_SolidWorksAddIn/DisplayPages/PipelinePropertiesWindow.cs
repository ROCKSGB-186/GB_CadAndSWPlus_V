using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;

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
        private bool _loadFailed;

        public IReadOnlyList<PipelinePropertiesFieldDefinition> Fields { get; }
        public PipelinePropertiesConfigurationService ConfigurationService { get; }
        /// <summary>仅表示窗口初始化时的读取结果；文件不存在视为正常首次运行。</summary>
        public bool ConfigurationLoadSucceeded { get; private set; }

        /// <summary>仅在确定并完成校验后产生，取消时为 null；调用方应在建模成功后再保存。</summary>
        public IDictionary<string, string> ConfirmedAttributes { get; private set; }

        /// <param name="fields">服务端目录转换后的字段；null 或空集合使用本地精简字段，不使用目录默认值。</param>
        /// <param name="initialAttributes">本次实例属性，优先于本地最后属性；起终点可为空。</param>
        /// <param name="configurationService">可选本地配置服务。</param>
        public PipelinePropertiesWindow(
            IEnumerable<PipelinePropertiesFieldDefinition> fields = null,
            IDictionary<string, string> initialAttributes = null,
            PipelinePropertiesConfigurationService configurationService = null)
        {
            SolidWorksFileLogger.Start("打开绘管属性窗口");
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

            Title = "SolidWorks 绘管属性";
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
                Text = catalogNotice + "\n属性和尺寸尚未进行工程规范校核；自由起终点无需连接设备。" +
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

            var panel = new StackPanel();
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
            if (!TryPositiveNumber(values["PIPE_OD"], out outerDiameter) ||
                !TryPositiveNumber(values["PIPE_THK"], out thickness) || thickness >= outerDiameter / 2)
            {
                SolidWorksFileLogger.Warning("绘管属性校验失败：外径或壁厚无效。");
                MessageBox.Show(this, "请输入有效的正数外径和壁厚（单位：mm），壁厚必须小于外径的一半。",
                    "绘管尺寸校验", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ConfirmedAttributes = values;
            SolidWorksFileLogger.Info("绘管属性确认通过；尚未保存，等待模型创建成功后由调用方处理。");
            DialogResult = true;
        }

        private static bool TryPositiveNumber(string text, out double value)
        {
            const NumberStyles styles = NumberStyles.Float;
            bool parsed = double.TryParse(text, styles, CultureInfo.InvariantCulture, out value) ||
                double.TryParse(text, styles, CultureInfo.CurrentCulture, out value);
            return parsed && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private static bool IsFreeEndpoint(string tag)
        {
            return string.Equals(tag, "START_POINT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tag, "END_POINT", StringComparison.OrdinalIgnoreCase);
        }
    }
}

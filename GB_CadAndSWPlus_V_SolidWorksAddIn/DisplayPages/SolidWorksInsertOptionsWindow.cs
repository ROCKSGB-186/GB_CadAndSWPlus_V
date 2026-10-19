using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>双击模型按键后用于选择模型配置和显示状态。</summary>
    internal sealed class SolidWorksInsertOptionsWindow : Window
    {
        private readonly ComboBox _configurationComboBox;
        private readonly ComboBox _displayStateComboBox;
        private readonly IDictionary<string, IList<string>> _displayStatesByConfiguration;
        private bool _initializing;

        public string SelectedConfiguration => _configurationComboBox.SelectedItem as string ?? string.Empty;

        public string SelectedDisplayState =>
            (_displayStateComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

        public SolidWorksInsertOptionsWindow(
            IDictionary<string, IList<string>> displayStatesByConfiguration,
            string selectedConfiguration,
            string selectedDisplayState)
        {
            _displayStatesByConfiguration = displayStatesByConfiguration ??
                new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

            Title = "SolidWorks 模型插入设置";
            Width = 410;
            Height = 235;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var layout = new Grid { Margin = new Thickness(18) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            layout.Children.Add(CreateLabel("模型配置：", 0, 0));
            _configurationComboBox = new ComboBox { MinWidth = 250, Margin = new Thickness(0, 4, 0, 8) };
            Grid.SetRow(_configurationComboBox, 0);
            Grid.SetColumn(_configurationComboBox, 1);
            layout.Children.Add(_configurationComboBox);

            layout.Children.Add(CreateLabel("显示状态：", 1, 0));
            _displayStateComboBox = new ComboBox { MinWidth = 250, Margin = new Thickness(0, 4, 0, 8) };
            Grid.SetRow(_displayStateComboBox, 1);
            Grid.SetColumn(_displayStateComboBox, 1);
            layout.Children.Add(_displayStateComboBox);

            var description = new TextBlock
            {
                Text = "所选配置和显示状态将应用到本次插入的模型。",
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(description, 2);
            Grid.SetColumnSpan(description, 2);
            layout.Children.Add(description);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var insertButton = new Button { Content = "插入", Width = 78, Margin = new Thickness(4), IsDefault = true };
            insertButton.Click += (sender, args) => DialogResult = true;
            var cancelButton = new Button { Content = "取消", Width = 78, Margin = new Thickness(4), IsCancel = true };
            buttons.Children.Add(insertButton);
            buttons.Children.Add(cancelButton);
            Grid.SetRow(buttons, 4);
            Grid.SetColumnSpan(buttons, 2);
            layout.Children.Add(buttons);
            Content = layout;

            _configurationComboBox.SelectionChanged += ConfigurationComboBox_SelectionChanged;
            _initializing = true;
            foreach (string configuration in _displayStatesByConfiguration.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                _configurationComboBox.Items.Add(configuration);
            _configurationComboBox.SelectedItem = _displayStatesByConfiguration.ContainsKey(selectedConfiguration)
                ? selectedConfiguration
                : _configurationComboBox.Items.Cast<string>().FirstOrDefault();
            _initializing = false;
            UpdateDisplayStates(selectedDisplayState);
        }

        private static TextBlock CreateLabel(string text, int row, int column)
        {
            var label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 8)
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            return label;
        }

        private void ConfigurationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initializing)
                UpdateDisplayStates(string.Empty);
        }

        private void UpdateDisplayStates(string preferredDisplayState)
        {
            _displayStateComboBox.Items.Clear();
            var defaultState = new ComboBoxItem { Content = "模型默认", Tag = string.Empty };
            _displayStateComboBox.Items.Add(defaultState);

            string configuration = SelectedConfiguration;
            if (_displayStatesByConfiguration.TryGetValue(configuration, out IList<string> displayStates))
            {
                foreach (string displayState in displayStates.Where(name => !string.IsNullOrWhiteSpace(name)))
                    _displayStateComboBox.Items.Add(new ComboBoxItem { Content = displayState, Tag = displayState });
            }

            ComboBoxItem preferredItem = _displayStateComboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, preferredDisplayState, StringComparison.OrdinalIgnoreCase));
            _displayStateComboBox.SelectedItem = preferredItem ?? defaultState;
            _displayStateComboBox.IsEnabled = _displayStateComboBox.Items.Count > 1;
        }
    }
}

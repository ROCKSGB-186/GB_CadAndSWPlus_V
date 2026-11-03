using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>确认以毫米显示的空间点；接口捕捉时保留接口的精确位置。</summary>
    internal sealed class PipelinePointConfirmWindow : Window
    {
        private readonly PipePoint3D _original;
        private readonly TextBox _x;
        private readonly TextBox _y;
        private readonly TextBox _z;
        private readonly TextBox _step;

        public PipelinePointConfirmWindow(PipePoint3D original, PipePoint3D? previous, bool isConnectionPoint)
        {
            _original = original;
            Title = "确认管道坐标";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock
            {
                Text = "拾取坐标（mm）： " + Describe(original),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            root.Children.Add(new TextBlock
            {
                Text = isConnectionPoint
                    ? "已选择接口：为保持连接准确，坐标不进行取整或编辑。"
                    : "可修改建议坐标。三轴取整可能改变当前工作平面的固定坐标。",
                Foreground = Brushes.DarkOrange,
                TextWrapping = TextWrapping.Wrap
            });
            _x = AddTextInput(root, "X（mm）", Format(original.X), !isConnectionPoint);
            _y = AddTextInput(root, "Y（mm）", Format(original.Y), !isConnectionPoint);
            _z = AddTextInput(root, "Z（mm）", Format(original.Z), !isConnectionPoint);
            _step = AddTextInput(root, "对齐步长（mm）", "50", !isConnectionPoint);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var snap = new Button { Content = "按步长对齐", Margin = new Thickness(4), IsEnabled = !isConnectionPoint };
            snap.Click += SnapClick;
            var originalButton = new Button { Content = "采用原值", Margin = new Thickness(4), IsEnabled = !isConnectionPoint };
            originalButton.Click += (sender, args) => SetPoint(_original);
            var confirm = new Button { Content = "确认坐标", Margin = new Thickness(4), IsDefault = true };
            confirm.Click += ConfirmClick;
            var cancel = new Button { Content = "返回选点", Margin = new Thickness(4), IsCancel = true };
            cancel.Click += (sender, args) => SolidWorksFileLogger.Info("取消本次坐标确认，返回选点。");
            buttons.Children.Add(snap);
            buttons.Children.Add(originalButton);
            buttons.Children.Add(confirm);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            root.Children.Add(new TextBlock
            {
                Text = previous.HasValue ? "上次确认（mm）： " + Describe(previous.Value) : "上次确认：尚无",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            Content = root;

            if (!isConnectionPoint)
            {
                try
                {
                    SetPoint(PipeRouteMath.SnapPoint(original, 50m));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is ArithmeticException)
                {
                    SolidWorksFileLogger.Warning("默认坐标对齐失败，保留原始坐标：" + ex.Message);
                    root.Children.Add(new TextBlock
                    {
                        Text = "无法按默认步长对齐，当前保留原始坐标；您可以手动输入。",
                        Foreground = Brushes.DarkOrange,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
            }
            SolidWorksFileLogger.Info("打开管道坐标确认窗口；端点类型=" + (isConnectionPoint ? "接口" : "自由点") + "。");
        }

        public PipePoint3D ConfirmedPoint { get; private set; }

        private static TextBox AddTextInput(Panel panel, string label, string value, bool editable)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var text = new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(text, Dock.Left);
            row.Children.Add(text);
            var input = new TextBox { Text = value, IsReadOnly = !editable, MinWidth = 150 };
            row.Children.Add(input);
            panel.Children.Add(row);
            return input;
        }

        private void SnapClick(object sender, RoutedEventArgs e)
        {
            if (!TryReadPoint(out PipePoint3D point)) return;
            if (!decimal.TryParse(_step.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal step) || step <= 0m)
            {
                MessageBox.Show(this, "请输入大于零的有效对齐步长，单位为毫米。", "坐标确认", MessageBoxButton.OK, MessageBoxImage.Warning);
                _step.Focus();
                return;
            }

            try
            {
                SetPoint(PipeRouteMath.SnapPoint(point, step));
                SolidWorksFileLogger.Info("管道候选坐标已按用户指定步长对齐。");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is ArithmeticException)
            {
                SolidWorksFileLogger.Warning("管道候选坐标对齐失败：" + ex.Message);
                MessageBox.Show(this, "坐标无法按该步长对齐：" + ex.Message, "坐标确认", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ConfirmClick(object sender, RoutedEventArgs e)
        {
            if (!TryReadPoint(out PipePoint3D point)) return;
            ConfirmedPoint = point;
            SolidWorksFileLogger.Info("已确认管道坐标；单位=毫米。");
            DialogResult = true;
        }

        private bool TryReadPoint(out PipePoint3D point)
        {
            if (!TryReadNumber(_x.Text, out double x) ||
                !TryReadNumber(_y.Text, out double y) ||
                !TryReadNumber(_z.Text, out double z))
            {
                point = default(PipePoint3D);
                SolidWorksFileLogger.Warning("管道坐标确认失败：坐标不是有效有限数值。");
                MessageBox.Show(this, "请输入有效的 X、Y、Z 数值，单位为毫米。", "坐标确认", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            point = new PipePoint3D(x, y, z);
            return true;
        }

        private static bool TryReadNumber(string text, out double value)
        {
            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            return parsed && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void SetPoint(PipePoint3D point)
        {
            _x.Text = Format(point.X);
            _y.Text = Format(point.Y);
            _z.Text = Format(point.Z);
        }

        private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        private static string Describe(PipePoint3D point) =>
            "X: " + Format(point.X) + "   Y: " + Format(point.Y) + "   Z: " + Format(point.Z);
    }
}

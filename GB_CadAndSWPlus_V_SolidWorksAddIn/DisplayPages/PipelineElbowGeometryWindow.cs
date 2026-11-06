using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>仅试算三点直角路线，不创建或修改 SolidWorks 模型。</summary>
    internal sealed class PipelineElbowGeometryWindow : Window
    {
        private readonly PipeRoute _route;
        private readonly TextBox _radius;
        private readonly TextBox _result;

        public PipelineElbowGeometryWindow(PipeRoute route)
        {
            _route = route ?? throw new ArgumentNullException(nameof(route));
            Title = "三点直角路线几何试算";
            Width = 560;
            Height = 470;
            MinWidth = 480;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(12) };
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock
            {
                Text = "仅计算三点 90° 中心线，不生成或修改模型，也不保存文件。请自行输入弯头中心线半径；试算不代表标准管件选型、规格校核或碰撞检查。",
                Foreground = Brushes.DarkOrange,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var label = new TextBlock { Text = "弯头中心线半径（mm）：", VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            _radius = new TextBox { MinWidth = 120 };
            row.Children.Add(_radius);
            heading.Children.Add(row);
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var calculate = new Button { Content = "试算", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
            calculate.Click += Calculate;
            var close = new Button { Content = "关闭", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsCancel = true };
            close.Click += (sender, args) => Close();
            buttons.Children.Add(calculate);
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _result = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "请输入半径后点击“试算”。"
            };
            _radius.TextChanged += (sender, args) => _result.Text = "半径已更改，请重新试算。";
            root.Children.Add(_result);
            Content = root;
        }

        private void Calculate(object sender, RoutedEventArgs args)
        {
            if (!(double.TryParse(_radius.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double radius) ||
                  double.TryParse(_radius.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out radius)) ||
                double.IsNaN(radius) || double.IsInfinity(radius) || radius <= 0)
            {
                _result.Text = "试算未完成，请输入大于零的有限中心线半径。";
                SolidWorksFileLogger.Warning("三点直角路线试算失败：中心线半径无效。");
                MessageBox.Show(this, "请输入大于零的有限中心线半径，单位为毫米。",
                    "三点直角路线试算", MessageBoxButton.OK, MessageBoxImage.Warning);
                _radius.Focus();
                return;
            }

            try
            {
                PipelineElbowGeometryResult geometry = PipelineElbowGeometry.Calculate(_route, radius);
                var result = new StringBuilder();
                result.AppendLine("单位：mm；三个点表示中心线理论路径。");
                result.AppendLine("起点：" + Describe(geometry.Start));
                result.AppendLine("理论转角点：" + Describe(geometry.Corner));
                result.AppendLine("终点：" + Describe(geometry.End));
                result.AppendLine("入口切点：" + Describe(geometry.EntryTangent));
                result.AppendLine("出口切点：" + Describe(geometry.ExitTangent));
                result.AppendLine("弯曲圆心：" + Describe(geometry.Center));
                result.AppendLine("中心线半径：" + Format(geometry.RadiusMm));
                result.AppendLine("入口直段中心线长度：" + Format(geometry.InletStraightLengthMm));
                result.AppendLine("出口直段中心线长度：" + Format(geometry.OutletStraightLengthMm));
                result.AppendLine("弯曲中心线长度：" + Format(geometry.ArcLengthMm));
                result.AppendLine("总中心线长度：" + Format(geometry.TotalLengthMm));
                result.AppendLine("以上仅为理论几何试算，不代表管件标准尺寸或实际下料长度；未生成模型。");
                _result.Text = result.ToString();
                SolidWorksFileLogger.Info("三点直角路线几何试算成功；未修改模型或保存文件。");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ArithmeticException)
            {
                _result.Text = "试算未完成：" + ex.Message;
                SolidWorksFileLogger.Warning("三点直角路线几何试算失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "三点直角路线试算",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        private static string Describe(PipePoint3D point) =>
            "X=" + Format(point.X) + "  Y=" + Format(point.Y) + "  Z=" + Format(point.Z);
    }
}

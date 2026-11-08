using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>三点弯头路线试算及独立弯头试验。</summary>
    internal sealed class PipelineElbowGeometryWindow : Window
    {
        private PipeRoute _route;
        private readonly Action<PipeRoute>? _routeChanged;
        private readonly IDictionary<string, string>? _attributes;
        private readonly SldWorks _application;
        private readonly ModelDoc2 _sourceDocument;
        private readonly IntPtr _mainWindow;
        private readonly TextBox _radius;
        private readonly TextBox _result;
        private readonly ComboBox _targetAngle;
        private readonly Button _adjustAngle;
        private readonly Button _testElbow;
        private readonly Button _saveElbow;
        private bool _building;

        public PipelineElbowGeometryWindow(PipeRoute route, IDictionary<string, string>? attributes,
            SldWorks application, ModelDoc2 sourceDocument, IntPtr mainWindow, Action<PipeRoute>? routeChanged = null)
        {
            _route = route ?? throw new ArgumentNullException(nameof(route));
            _routeChanged = routeChanged;
            _attributes = attributes == null ? null : new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _sourceDocument = sourceDocument ?? throw new ArgumentNullException(nameof(sourceDocument));
            if (mainWindow == IntPtr.Zero) throw new ArgumentException("SolidWorks 主窗口句柄无效。", nameof(mainWindow));
            _mainWindow = mainWindow;
            Title = "三点弯头路线几何试算";
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
                Text = "试运行支持 45°、90°、135° 转向角；其他角度可选择调整方案。独立弯头不插入装配体或写入属性；结果不代表标准件选型、规范校核或碰撞检查。",
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
            var adjustmentRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            adjustmentRow.Children.Add(new TextBlock { Text = "目标转向角：", VerticalAlignment = VerticalAlignment.Center });
            _targetAngle = new ComboBox { Width = 75, Margin = new Thickness(0, 0, 8, 0), ItemsSource = new[] { 45.0, 90.0, 135.0 }, SelectedIndex = 1 };
            adjustmentRow.Children.Add(_targetAngle);
            _adjustAngle = new Button { Content = "查看角度调整方案", Padding = new Thickness(6, 2, 6, 2) };
            _adjustAngle.Click += (sender, args) => ShowAngleAdjustment();
            adjustmentRow.Children.Add(_adjustAngle);
            heading.Children.Add(adjustmentRow);
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var calculate = new Button { Content = "试算", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
            calculate.Click += Calculate;
            var pieces = new Button { Content = "三零件尺寸核验", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
            pieces.Click += CalculatePieces;
            _testElbow = new Button { Content = "验证独立弯头", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
            _testElbow.Click += (sender, args) => RunElbowTrial(false);
            _saveElbow = new Button { Content = "保存独立弯头", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
            _saveElbow.Click += (sender, args) => RunElbowTrial(true);
            var close = new Button { Content = "关闭", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsCancel = true };
            close.Click += (sender, args) => Close();
            buttons.Children.Add(calculate);
            buttons.Children.Add(pieces);
            buttons.Children.Add(_testElbow);
            buttons.Children.Add(_saveElbow);
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
            Closing += PreventCloseDuringBuild;
        }

        private void Calculate(object sender, RoutedEventArgs args)
        {
            if (!TryReadRadius(out double radius)) return;

            try
            {
                PipelineElbowGeometryResult geometry = PipelineElbowGeometry.Calculate(_route, radius);
                var result = new StringBuilder();
                result.AppendLine("单位：mm；三个点表示中心线理论路径，角度为前进方向的转向角。");
                result.AppendLine("转向角：" + Format(geometry.TurnAngleDegrees) + "°");
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
                SolidWorksFileLogger.Info("三点弯头路线几何试算成功；转向角=" + Format(geometry.TurnAngleDegrees) + "°；未修改模型或保存文件。");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ArithmeticException)
            {
                _result.Text = "试算未完成：" + ex.Message;
                SolidWorksFileLogger.Warning("三点弯头路线几何试算失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "三点弯头路线试算",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowAngleAdjustment()
        {
            if (!(_targetAngle.SelectedItem is double target))
            {
                SolidWorksFileLogger.Warning("角度调整未启动：尚未选择目标转向角。");
                MessageBox.Show(this, "请选择目标转向角。", "角度调整", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_routeChanged == null)
            {
                SolidWorksFileLogger.Warning("角度调整未启动：无法更新原路线。");
                MessageBox.Show(this, "无法更新原路线，请关闭窗口后重新打开。", "角度调整", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                PipelineAngleAdjustmentResult proposal = PipelineAngleAdjustment.Calculate(_route, target);
                var dialog = new Window
                {
                    Title = "选择路线角度调整方案",
                    Owner = this,
                    Width = 700,
                    Height = 410,
                    MinWidth = 540,
                    MinHeight = 330,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false
                };
                var panel = new DockPanel { Margin = new Thickness(12) };
                var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
                var moveEnd = new Button { Content = "选择调整终点", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsEnabled = proposal.MoveEndRoute != null };
                var moveCorner = new Button { Content = "选择调整转角点", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsEnabled = proposal.MoveCornerRoute != null };
                var cancel = new Button { Content = "取消", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsCancel = true };
                moveEnd.Click += (sender, args) => ApplyAngleAdjustment(dialog, proposal.MoveEndRoute!, "终点", target);
                moveCorner.Click += (sender, args) => ApplyAngleAdjustment(dialog, proposal.MoveCornerRoute!, "转角点", target);
                cancel.Click += (sender, args) => dialog.Close();
                buttons.Children.Add(moveEnd);
                buttons.Children.Add(moveCorner);
                buttons.Children.Add(cancel);
                DockPanel.SetDock(buttons, Dock.Bottom);
                panel.Children.Add(buttons);
                var text = new StringBuilder();
                text.AppendLine("角度为前进方向的转向角；单位：度、mm。");
                text.AppendLine("当前角度：" + Format(proposal.OriginalAngleDegrees) + "°；目标角度：" + Format(proposal.TargetAngleDegrees) + "°。");
                text.AppendLine("原起点：" + Describe(_route.Points[0]));
                text.AppendLine("原转角点：" + Describe(_route.Points[1]));
                text.AppendLine("原终点：" + Describe(_route.Points[2]));
                text.AppendLine();
                text.AppendLine(proposal.MoveEndRoute == null
                    ? "方案一不可用：" + proposal.MoveEndUnavailableReason
                    : "方案一：保持起点、转角点及出口段长度，调整终点为 " + Describe(proposal.MoveEndRoute.Points[2]));
                text.AppendLine(proposal.MoveCornerRoute == null
                    ? "方案二不可用：" + proposal.MoveCornerUnavailableReason
                    : "方案二：保持起点、终点，调整转角点为 " + Describe(proposal.MoveCornerRoute.Points[1]));
                text.AppendLine();
                text.AppendLine("候选仅用于当前三点自由端路线；在原路线平面内调整，不修改已创建的模型。应用后还需重新核验半径和直管长度。");
                panel.Children.Add(new TextBox
                {
                    Text = text.ToString(), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                });
                dialog.Content = panel;
                SolidWorksFileLogger.Info("已生成角度调整候选；当前角度=" + Format(proposal.OriginalAngleDegrees) +
                    "°；目标角度=" + Format(target) + "°；尚未修改路线。");
                dialog.ShowDialog();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ArithmeticException)
            {
                SolidWorksFileLogger.Warning("角度调整方案计算失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "角度调整", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplyAngleAdjustment(Window dialog, PipeRoute adjusted, string changedPoint, double target)
        {
            int index = changedPoint == "终点" ? 2 : 1;
            if (MessageBox.Show(dialog,
                "目标转向角：" + Format(target) + "°。\n" + changedPoint + "原坐标：" + Describe(_route.Points[index]) +
                "\n调整后坐标：" + Describe(adjusted.Points[index]) +
                "\n将更新本次路线坐标，不会修改已创建的模型。是否应用？",
                "确认路线角度调整", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                SolidWorksFileLogger.Info("用户取消应用角度调整，路线未改变。");
                return;
            }
            try
            {
                _routeChanged!(adjusted);
                _route = adjusted;
                _result.Text = "已按用户确认更新路线；请重新试算及核验尺寸。";
                SolidWorksFileLogger.Info("用户确认角度调整；目标转向角=" + Format(target) + "°；调整" + changedPoint +
                    "为 " + Describe(adjusted.Points[index]) + "；未修改已有模型。");
                dialog.Close();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                SolidWorksFileLogger.Warning("应用角度调整失败：" + ex.Message);
                MessageBox.Show(dialog, "无法更新路线：" + ex.Message, "角度调整", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CalculatePieces(object sender, RoutedEventArgs args)
        {
            if (!TryCreatePlan(out PipelineThreePiecePlan? plan)) return;
            var result = new StringBuilder();
            result.AppendLine("仅为实际输入尺寸的理论方案；单位：mm。");
            result.AppendLine("第一根直管：" + Describe(plan!.InletRoute.Points[0]) + " → " + Describe(plan.InletRoute.Points[1]));
            result.AppendLine("第一根直管中心线长度：" + Format(plan.Elbow.InletStraightLengthMm));
            result.AppendLine("简化弯头入口：" + Describe(plan.Elbow.EntryTangent));
            result.AppendLine("简化弯头出口：" + Describe(plan.Elbow.ExitTangent));
            result.AppendLine("简化弯头中心线圆心：" + Describe(plan.Elbow.Center));
            result.AppendLine("弯头中心线弧长：" + Format(plan.Elbow.ArcLengthMm));
            result.AppendLine("转向角：" + Format(plan.Elbow.TurnAngleDegrees) + "°");
            result.AppendLine("第二根直管：" + Describe(plan.OutletRoute.Points[0]) + " → " + Describe(plan.OutletRoute.Points[1]));
            result.AppendLine("第二根直管中心线长度：" + Format(plan.Elbow.OutletStraightLengthMm));
            result.AppendLine("外径：" + Format(plan.OutsideDiameterMm) + "；壁厚：" + Format(plan.WallThicknessMm) +
                "；内径：" + Format(plan.InsideDiameterMm));
            result.AppendLine("弯头中心线半径：" + Format(plan.Elbow.RadiusMm));
            result.AppendLine("本次只核验几何尺寸，不代表标准件或工程规范校核；未生成、保存或插入零件。");
            _result.Text = result.ToString();
            SolidWorksFileLogger.Info("三零件理论尺寸核验成功；未修改模型或保存文件。");
        }

        private bool TryCreatePlan(out PipelineThreePiecePlan? plan)
        {
            plan = null;
            if (!TryReadRadius(out double radius)) return false;
            if (_attributes == null)
            {
                _result.Text = "三零件尺寸核验未完成：请返回路线窗口，打开管道属性并点击“确定”。";
                SolidWorksFileLogger.Warning("三零件尺寸核验未启动：尚未确认管道属性。");
                MessageBox.Show(this, _result.Text, "三零件尺寸核验", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (!PipelineStraightParameters.TryCreate(_attributes, out PipelineStraightParameters? parameters, out string error))
            {
                _result.Text = "三零件尺寸核验未完成：" + error;
                SolidWorksFileLogger.Warning("三零件尺寸核验未启动：" + error);
                MessageBox.Show(this, error, "三零件尺寸核验", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            try
            {
                plan = PipelineThreePiecePlan.Create(
                    _route, radius, parameters!.OutsideDiameterMm, parameters.WallThicknessMm);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ArithmeticException)
            {
                _result.Text = "三零件尺寸核验未完成：" + ex.Message;
                SolidWorksFileLogger.Warning("三零件尺寸核验失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "三零件尺寸核验", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void RunElbowTrial(bool saveModel)
        {
            if (_building)
            {
                SolidWorksFileLogger.Warning("独立弯头验证正在进行，拒绝重复启动。");
                return;
            }
            if (!TryCreatePlan(out PipelineThreePiecePlan? plan)) return;
            PipelineElbowModelResult? model = null;
            string? destination = null;
            bool saved = false;
            _building = true;
            _testElbow.IsEnabled = false;
            _saveElbow.IsEnabled = false;
            _adjustAngle.IsEnabled = false;
            _targetAngle.IsEnabled = false;
            try
            {
                RequireSourceDocument();
                PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                string template = _application.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                if (string.IsNullOrWhiteSpace(template) || !File.Exists(template) ||
                    !string.Equals(Path.GetExtension(template), ".prtdot", StringComparison.OrdinalIgnoreCase))
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "选择用于独立弯头验证的零件模板",
                        Filter = "SolidWorks 零件模板 (*.prtdot)|*.prtdot",
                        CheckFileExists = true,
                        Multiselect = false
                    };
                    if (dialog.ShowDialog(this) != true)
                    {
                        _result.Text = "已取消选择零件模板，未创建弯头。";
                        SolidWorksFileLogger.Info("用户取消选择弯头验证模板，未创建零件。");
                        return;
                    }
                    template = dialog.FileName;
                }
                if (saveModel)
                {
                    var saveDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "选择新的独立弯头零件文件",
                        Filter = "SolidWorks 零件 (*.sldprt)|*.sldprt",
                        DefaultExt = ".sldprt",
                        AddExtension = true,
                        CheckPathExists = true,
                        OverwritePrompt = false,
                        FileName = "弯头.sldprt"
                    };
                    if (saveDialog.ShowDialog(this) != true)
                    {
                        _result.Text = "已取消选择保存路径，未创建弯头。";
                        SolidWorksFileLogger.Info("用户取消选择弯头保存路径，未创建零件。");
                        return;
                    }
                    destination = PipelineStraightModelSaver.ValidatePath(saveDialog.FileName);
                    if (File.Exists(destination) || Directory.Exists(destination))
                        throw new IOException("目标文件或目录已存在，未覆盖；请选择新的文件名。");
                    if (_application.GetOpenDocumentByName(destination) != null)
                        throw new InvalidOperationException("目标路径已被 SolidWorks 文档使用，请选择其他文件名。");
                }
                string action = saveModel
                    ? "将新建独立弯头，并尝试保存到新文件：\n" + destination +
                      "\n仅用于专用空测试文件夹，不作正式文件；保存期间请勿让其他程序或人员访问目标文件。\n" +
                      "试运行仅支持普通本机固定 NTFS 磁盘，不覆盖预先存在的文件；保存失败时可能留下空文件或不完整文件，请检查后再处理。\n"
                    : "将新建独立的未保存弯头测试零件。\n";
                if (MessageBox.Show(this,
                    action + "不会插入当前装配体，也不写入属性或数据库。\n" +
                    "失败时可能留下未保存或不完整的新零件，不会自动关闭或回滚；请勿用于正式工程。是否继续？",
                    "独立弯头验证", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    _result.Text = "已取消独立弯头验证，未创建模型。";
                    SolidWorksFileLogger.Info("用户取消独立弯头验证，未创建零件。");
                    return;
                }
                RequireSourceDocument();
                PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                model = PipelineElbowModelBuilder.Build(_application, _mainWindow, template, plan!);
                if (saveModel)
                {
                    PipelineStraightModelSaver.Save(_application, model.Document, _mainWindow, destination!, "弯头",
                        () => PipelineElbowModelBuilder.Revalidate(_application, model, plan!, destination!));
                    saved = true;
                    _result.Text = "独立弯头已保存并通过复验（当前打开文档的几何和文件状态）；尚未重新打开文件核验。未插入装配体，未写入属性或数据库。";
                }
                else
                {
                    _result.Text = "独立弯头验证完成；新零件未保存，未插入装配体，未写入属性或数据库。";
                }
                MessageBox.Show(this, _result.Text + "\n零件：" + model.Title +
                    (saved ? "\n保存路径：" + destination : string.Empty) +
                    "\n请检查模型；当前仅为试运行，不代表标准件或工程规范校核。",
                    "独立弯头验证", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (PipelineElbowModelBuildException ex)
            {
                _result.Text = "独立弯头验证失败：" + ex.Message;
                MessageBox.Show(this, _result.Text, "独立弯头验证失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (PipelineStraightModelSaveException ex)
            {
                _result.Text = "独立弯头保存未完全成功，请检查文件和零件后再操作。";
                MessageBox.Show(this, _result.Text + "\n" + ex.Message, "独立弯头保存失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (PipelineElbowModelRevalidationException ex)
            {
                SolidWorksFileLogger.Error("弯头已保存但复验失败：" + ex);
                _result.Text = "独立弯头已保存，但保存后复验未完成：" + ex.Message;
                MessageBox.Show(this, _result.Text + "\n保存路径：" + destination +
                    "\n请检查文件和零件；未自动关闭或回滚。", "独立弯头复验失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException ||
                ex is ArithmeticException || ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException ||
                ex is System.Runtime.InteropServices.InvalidComObjectException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                SolidWorksFileLogger.Error("独立弯头验证失败：" + ex);
                _result.Text = "独立弯头验证失败：" + ex.Message;
                string context = model == null ? "尚未完成新零件创建；请检查是否有新文档残留。" :
                    saved ? "弯头已保存到：\n" + destination + "\n后续复验未完成，请检查文件和零件；未自动关闭或回滚。" :
                    "新零件“" + model.Title + "”可能仍然打开，未自动关闭或回滚。";
                MessageBox.Show(this, _result.Text + "\n" + context, "独立弯头验证失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _building = false;
                _testElbow.IsEnabled = true;
                _saveElbow.IsEnabled = true;
                _adjustAngle.IsEnabled = true;
                _targetAngle.IsEnabled = true;
            }
        }

        private void RequireSourceDocument()
        {
            var active = _application.ActiveDoc as ModelDoc2;
            if (active == null || _application.IsSame(active, _sourceDocument) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("原路线文档不是活动文档。请关闭本试算窗口，切回原装配体或零件后重新打开试算窗口。");
        }

        private void PreventCloseDuringBuild(object? sender, CancelEventArgs args)
        {
            if (!_building) return;
            args.Cancel = true;
            _result.Text = "独立弯头验证尚未结束，请等待完成后再关闭窗口。";
            SolidWorksFileLogger.Warning("独立弯头验证尚未结束，暂不能关闭试算窗口。");
        }

        private bool TryReadRadius(out double radius)
        {
            if ((double.TryParse(_radius.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                 double.TryParse(_radius.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out radius)) &&
                !double.IsNaN(radius) && !double.IsInfinity(radius) && radius > 0)
                return true;
            _result.Text = "试算未完成，请输入大于零的有限中心线半径。";
            SolidWorksFileLogger.Warning("三点弯头路线试算失败：中心线半径无效。");
            MessageBox.Show(this, "请输入大于零的有限中心线半径，单位为毫米。",
                "三点弯头路线试算", MessageBoxButton.OK, MessageBoxImage.Warning);
            _radius.Focus();
            return false;
        }

        private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        private static string Describe(PipePoint3D point) =>
            "X=" + Format(point.X) + "  Y=" + Format(point.Y) + "  Z=" + Format(point.Z);
    }
}

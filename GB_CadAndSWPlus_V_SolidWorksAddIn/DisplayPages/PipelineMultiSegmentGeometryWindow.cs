using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>多点路线几何试算及独立零件试验。</summary>
    internal sealed class PipelineMultiSegmentGeometryWindow : Window
    {
        // 离线准备阶段保持双重阻断，实机试验须另行确认。
        private static readonly bool ExistingAssemblyTrialEnabled = false;
        private readonly PipeRoute _route;
        private readonly IDictionary<string, string>? _attributes;
        private readonly Func<string, bool> _confirmApproximation;
        private readonly TextBox _radius;
        private readonly TextBox _result;
        private readonly Button _calculate;
        private readonly Button _build;
        private readonly Button _save;
        private readonly Button _assembly;
        private readonly Button _existingAssembly;
        private readonly Button _close;
        private readonly SldWorks? _application;
        private readonly ModelDoc2? _sourceDocument;
        private readonly IntPtr _mainWindow;
        private readonly PipelineAssemblyRouteGuard? _routeGuard;
        private readonly Func<bool>? _canAttemptInsertion;
        private readonly Action? _markInsertionAttempted;
        private bool _building;

        internal PipelineMultiSegmentGeometryWindow(PipeRoute route, IDictionary<string, string>? attributes)
            : this(route, attributes, null)
        {
        }

        internal PipelineMultiSegmentGeometryWindow(PipeRoute route, IDictionary<string, string>? attributes,
            SldWorks application, ModelDoc2 sourceDocument, IntPtr mainWindow)
            : this(route, attributes, application, sourceDocument, mainWindow, null, null, null)
        {
        }

        internal PipelineMultiSegmentGeometryWindow(PipeRoute route, IDictionary<string, string>? attributes,
            SldWorks application, ModelDoc2 sourceDocument, IntPtr mainWindow, PipelineAssemblyRouteGuard? routeGuard,
            Func<bool>? canAttemptInsertion, Action? markInsertionAttempted)
            : this(route, attributes, null)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _sourceDocument = sourceDocument ?? throw new ArgumentNullException(nameof(sourceDocument));
            if (mainWindow == IntPtr.Zero) throw new ArgumentException("SolidWorks 主窗口句柄无效。", nameof(mainWindow));
            _mainWindow = mainWindow;
            _routeGuard = routeGuard;
            _canAttemptInsertion = canAttemptInsertion;
            _markInsertionAttempted = markInsertionAttempted;
            _build.IsEnabled = true;
            _save.IsEnabled = true;
            _assembly.IsEnabled = true;
            _existingAssembly.IsEnabled = CanAttemptExistingAssembly();
        }

        internal PipelineMultiSegmentGeometryWindow(PipeRoute route, IDictionary<string, string>? attributes,
            Func<string, bool>? confirmApproximation)
        {
            _route = route ?? throw new ArgumentNullException(nameof(route));
            _attributes = attributes == null ? null : new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase);
            _confirmApproximation = confirmApproximation ?? (message => MessageBox.Show(this, message, "确认近似预览",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
            Title = "多段管道几何试算";
            Width = 700;
            Height = 540;
            MinWidth = 480;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(12) };
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock
            {
                Text = "几何试算不创建模型；独立零件和新建空装配体试验需另行确认。保存或装配试验会重新新建零件，不修改以前的测试文件。统一使用本次确认的管径、壁厚和输入半径，近似路线仅供预览，不支持建模。不检查整体自交、干涉或工程规范。",
                Foreground = Brushes.DarkOrange,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = "弯头中心线半径（mm）：", VerticalAlignment = VerticalAlignment.Center });
            _radius = new TextBox { MinWidth = 120 };
            row.Children.Add(_radius);
            heading.Children.Add(row);
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);

            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            _calculate = new Button { Content = "试算", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3) };
            _calculate.Click += (sender, args) => Calculate();
            buttons.Children.Add(_calculate);
            _build = new Button { Content = "验证多段独立零件", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsEnabled = false };
            _build.Click += (sender, args) => BuildPieces();
            buttons.Children.Add(_build);
            _save = new Button { Content = "保存多段独立零件", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsEnabled = false };
            _save.Click += (sender, args) => SavePieces();
            buttons.Children.Add(_save);
            _assembly = new Button { Content = "多段空装配体试验", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsEnabled = false };
            _assembly.Click += (sender, args) => AssemblePieces();
            buttons.Children.Add(_assembly);
            _existingAssembly = new Button { Content = "双零件装配体多段试验（筹备中）", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsEnabled = false };
            _existingAssembly.Click += (sender, args) => AssembleIntoExisting();
            buttons.Children.Add(_existingAssembly);
            _close = new Button { Content = "关闭", Margin = new Thickness(4), Padding = new Thickness(8, 3, 8, 3), IsCancel = true };
            _close.Click += (sender, args) => Close();
            buttons.Children.Add(_close);
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

        private void Calculate()
        {
            if (!TryCreatePlan(false, out PipelineMultiSegmentPlan? plan, out double radius)) return;
            var result = new StringBuilder();
            result.AppendLine("单位：mm；以下为理论中心线，不代表实际下料长度。");
            if (plan!.ApproximatePointNumbers.Count > 0)
                result.AppendLine("近似预览：路线点 " + string.Join("、", plan.ApproximatePointNumbers) +
                    "；最大偏移 " + Format(plan.MaximumApproximationMm) + " mm；原始坐标未改变。");
            result.AppendLine("外径：" + Format(plan.OutsideDiameterMm) + "；壁厚：" + Format(plan.WallThicknessMm) +
                "；内径：" + Format(plan.InsideDiameterMm) + "；弯头中心线半径：" + Format(radius));
            try
            {
                for (int i = 0; i < plan.Straights.Count; i++)
                {
                    PipeRoute straight = plan.Straights[i];
                    result.AppendLine("直管 " + (i + 1) + "：" + Describe(straight.Points[0]) + " → " + Describe(straight.Points[1]));
                    result.AppendLine("中心线长度：" + Format(Length(straight.Points[0], straight.Points[1])));
                    if (i < plan.Elbows.Count)
                    {
                        var elbow = plan.Elbows[i];
                        result.AppendLine("弯头 " + (i + 1) + "（路线点 " + plan.ElbowPointNumbers[i] + "）：转向角 " +
                            Format(elbow.TurnAngleDegrees) + "°；弧长 " + Format(elbow.ArcLengthMm));
                        result.AppendLine("入口切点：" + Describe(elbow.EntryTangent));
                        result.AppendLine("出口切点：" + Describe(elbow.ExitTangent));
                        result.AppendLine("圆心：" + Describe(elbow.Center));
                    }
                }
                result.AppendLine("未生成模型；尚未检查整体自交、干涉、标准件选型或工程规范。");
                _result.Text = result.ToString();
                SolidWorksFileLogger.Info("多段管道几何试算完成；直管数=" + plan.Straights.Count +
                    "；弯头数=" + plan.Elbows.Count + "；未修改模型或保存文件。");
            }
            catch (ArithmeticException ex)
            {
                ShowError(ex.Message);
            }
        }

        private bool TryCreatePlan(bool forBuild, out PipelineMultiSegmentPlan? plan, out double radius)
        {
            plan = null;
            if (!PipelineStraightParameters.TryPositiveNumber(_radius.Text, out radius))
            {
                ShowError("请输入大于零的有限中心线半径，单位为毫米。");
                _radius.Focus();
                return false;
            }
            if (_attributes == null)
            {
                ShowError("请返回路线窗口，打开管道属性并点击“确定”。");
                return false;
            }
            if (!PipelineStraightParameters.TryCreate(_attributes, out PipelineStraightParameters? parameters, out string error))
            {
                ShowError(error);
                return false;
            }

            try
            {
                plan = PipelineMultiSegmentPlan.Create(
                    _route, radius, parameters!.OutsideDiameterMm, parameters.WallThicknessMm, true);
                if (plan.ApproximatePointNumbers.Count > 0)
                {
                    if (forBuild)
                    {
                        ShowError("该路线包含近似共线点，只能用于理论预览；请调整坐标后再进行独立零件试验。");
                        plan = null;
                        return false;
                    }
                    string warning = "路线点 " + string.Join("、", plan.ApproximatePointNumbers) +
                        " 并非严格共线；本次理论预览按近似直线计算，最大偏移 " +
                        Format(plan.MaximumApproximationMm) + " mm。\n原始坐标不会改变，也不会生成模型。是否继续？";
                    if (!_confirmApproximation(warning))
                    {
                        _result.Text = "已取消近似预览；原始路线未改变，也未生成模型。";
                        SolidWorksFileLogger.Info("用户取消多段路线近似几何预览；原始路线未改变。");
                        plan = null;
                        return false;
                    }
                    SolidWorksFileLogger.Info("用户确认多段路线近似几何预览；最大偏移=" +
                        Format(plan.MaximumApproximationMm) + " mm；原始路线未改变。");
                }
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ArithmeticException)
            {
                ShowError(ex.Message);
                return false;
            }
        }

        private void BuildPieces()
        {
            if (_building || _application == null || _sourceDocument == null)
            {
                ShowError("试验正在进行或原路线文档不可用。");
                return;
            }
            if (!TryCreatePlan(true, out PipelineMultiSegmentPlan? plan, out _)) return;
            SetTrialRunning(true);
            try
            {
                RequireSourceDocument();
                PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                string? template = ChoosePartTemplate();
                if (template == null)
                {
                    _result.Text = "已取消选择零件模板，未创建零件。";
                    SolidWorksFileLogger.Info("用户取消选择多段独立零件试验模板。");
                    return;
                }
                int count = plan!.Straights.Count + plan.Elbows.Count;
                if (MessageBox.Show(this, "将依路线顺序新建 " + count + " 个未保存的独立测试零件。\n" +
                    "各零件仅在创建时分别验证，不做最终同时几何复验；请勿在试验期间修改文档。\n" +
                    "不会保存、装配、写属性或数据库，也不检查连接或干涉。\n" +
                    "失败时可能留下部分或不完整的未保存零件，不会自动关闭或回滚；请勿用于正式工程。是否继续？",
                    "多段独立零件试验", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    _result.Text = "已取消多段独立零件试验，未创建模型。";
                    SolidWorksFileLogger.Info("用户取消多段独立零件试验。");
                    return;
                }
                RequireSourceDocument();
                PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                PipelineMultiPieceModelTrialResult models = PipelineMultiPieceModelTrial.Build(
                    _application, _sourceDocument, _mainWindow, template, plan);
                _result.Text = "多段独立零件已分别生成并在创建时校验，未做最终同时几何复验；均未保存，尚未装配或检查连接、干涉。";
                MessageBox.Show(this, _result.Text + "\n零件（按路线顺序）：" + string.Join("、", models.Titles) +
                    "\n未写入属性或数据库。请逐个检查测试零件。", "多段独立零件试验",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (PipelineMultiPieceModelTrialException ex)
            {
                SolidWorksFileLogger.Error("多段独立零件试验未完成：" + ex);
                _result.Text = "多段独立零件试验未完成：" + ex.Message;
                MessageBox.Show(this, _result.Text, "多段独立零件试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException ||
                ex is ArithmeticException || ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException ||
                ex is System.Runtime.InteropServices.InvalidComObjectException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                SolidWorksFileLogger.Error("多段独立零件试验未完成；异常类型=" + ex.GetType().Name + "。");
                _result.Text = "多段独立零件试验未完成：" + ex.Message;
                MessageBox.Show(this, _result.Text + "\n请检查是否有新文档残留；未自动关闭或回滚。",
                    "多段独立零件试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { SetTrialRunning(false); }
        }

        private string? ChoosePartTemplate()
        {
            string template = _application!.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            if (!string.IsNullOrWhiteSpace(template) && File.Exists(template) &&
                string.Equals(Path.GetExtension(template), ".prtdot", StringComparison.OrdinalIgnoreCase))
                return template;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择用于多段独立零件试验的零件模板",
                Filter = "SolidWorks 零件模板 (*.prtdot)|*.prtdot",
                CheckFileExists = true,
                Multiselect = false
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private void SavePieces()
        {
            if (_building || _application == null || _sourceDocument == null)
            {
                ShowError("试验正在进行或原路线文档不可用。");
                return;
            }
            if (!TryCreatePlan(true, out PipelineMultiSegmentPlan? plan, out _)) return;
            SetTrialRunning(true);
            try
            {
                RequireSourceDocument();
                PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                string? template = ChoosePartTemplate();
                if (template == null)
                {
                    _result.Text = "已取消选择零件模板，未创建或保存零件。";
                    SolidWorksFileLogger.Info("用户取消选择多段保存试验模板。");
                    return;
                }
                string? folder = ChooseEmptyTestFolder();
                if (folder == null)
                {
                    _result.Text = "已取消选择测试文件夹，未创建或保存零件。";
                    SolidWorksFileLogger.Info("用户取消选择多段保存试验文件夹。");
                    return;
                }
                using (PipelineMultiPieceSaveConfirmation confirmation =
                    PipelineMultiPieceSaveTrial.PreparePaths(_application, _mainWindow, folder, plan!))
                {
                    IReadOnlyList<string> paths = confirmation.Paths;
                    if (MessageBox.Show(this, "将按路线顺序重新新建并保存 " + paths.Count + " 个独立测试零件：\n" +
                        string.Join("\n", paths) +
                        "\n不会保存或修改之前已打开的测试零件。仅使用本机专用空测试文件夹；试验期间必须确保其他程序和人员不访问或更改文件及目录，程序无法完全阻止并发写入。\n" +
                        "逐件创建、保存并复验当前打开的零件，不重新打开文件，也不保证最终同时几何一致。试验同步执行并反复校验文件，路线较长时可能耗时较长、界面暂时无响应。\n" +
                        "不装配、不写属性或数据库，不检查连接或干涉。失败可能留下已保存、空或不完整文件及未保存零件；不会自动删除、关闭或回滚。是否继续？",
                        "保存多段独立零件试验", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) != MessageBoxResult.Yes)
                    {
                        _result.Text = "已取消多段保存试验，未创建模型或保存零件文件。";
                        SolidWorksFileLogger.Info("用户取消多段独立零件保存试验；未创建模型或保存零件文件。");
                        return;
                    }
                    RequireSourceDocument();
                    PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                    PipelineMultiPieceSaveTrial.BuildAndSave(_application, _sourceDocument, _mainWindow, template, plan!, confirmation);
                    _result.Text = "多段独立零件已逐件保存并复验当前打开文档；未重新打开文件，未最终同时复验几何，也未装配或检查连接、干涉。";
                    MessageBox.Show(this, _result.Text + "\n文件（按路线顺序）：\n" + string.Join("\n", paths) +
                        "\n未写入属性或数据库；请检查文件和零件。", "保存多段独立零件试验",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (PipelineMultiPieceSaveTrialException ex)
            {
                SolidWorksFileLogger.Error("多段独立零件保存试验未完成：" + ex);
                _result.Text = "多段独立零件保存试验未完成：" + ex.Message;
                MessageBox.Show(this, _result.Text, "多段独立零件保存试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException ||
                ex is ArithmeticException || ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException ||
                ex is System.Runtime.InteropServices.InvalidComObjectException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                SolidWorksFileLogger.Error("多段独立零件保存试验未启动或未完成；异常类型=" + ex.GetType().Name + "。");
                _result.Text = "多段独立零件保存试验未完成：" + ex.Message;
                MessageBox.Show(this, _result.Text + "\n请检查是否有文件或文档残留；未自动删除、关闭或回滚。",
                    "多段独立零件保存试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { SetTrialRunning(false); }
        }

        private void AssemblePieces()
        {
            if (_building || _application == null || _sourceDocument == null)
            {
                ShowError("试验正在进行或原路线文档不可用。");
                return;
            }
            if (!TryCreatePlan(true, out PipelineMultiSegmentPlan? plan, out _)) return;
            SetTrialRunning(true);
            IReadOnlyList<string>? savedPaths = null;
            try
            {
                RequireSourceDocument();
                PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                string? partTemplate = ChoosePartTemplate();
                if (partTemplate == null)
                {
                    _result.Text = "已取消选择零件模板，未创建模型或保存零件文件。";
                    SolidWorksFileLogger.Info("用户取消选择多段装配试验零件模板。");
                    return;
                }
                string? assemblyTemplate = ChooseAssemblyTemplate();
                if (assemblyTemplate == null)
                {
                    _result.Text = "已取消选择装配体模板，未创建模型或保存零件文件。";
                    SolidWorksFileLogger.Info("用户取消选择多段装配试验装配体模板。");
                    return;
                }
                string? folder = ChooseEmptyTestFolder();
                if (folder == null)
                {
                    _result.Text = "已取消选择测试文件夹，未创建模型或保存零件文件。";
                    SolidWorksFileLogger.Info("用户取消选择多段装配试验文件夹。");
                    return;
                }
                using (PipelineMultiPieceSaveConfirmation confirmation =
                    PipelineMultiPieceSaveTrial.PreparePaths(_application, _mainWindow, folder, plan!))
                {
                    IReadOnlyList<string> paths = confirmation.Paths;
                    if (MessageBox.Show(this, "将先按路线顺序重新创建并保存 " + paths.Count + " 个独立测试零件：\n" +
                        string.Join("\n", paths) +
                        "\n然后新建一个未保存的空测试装配体并逐件插入；必要时只调整本次新增组件的变换以对齐路线坐标，不改变固定状态。不会使用、保存或修改以前的测试文件或源装配体。\n" +
                        "仅使用本机专用空测试文件夹；从确认到试验结束，其他程序和人员不得访问或更改文件及目录，程序无法完全阻止并发写入。路线较长时可能耗时较长、界面暂时无响应。\n" +
                        "不自动保存或主动重建装配体，不添加配合，不写属性或数据库；组件可能固定或浮动，未检查连接或干涉。\n" +
                        "失败可能留下部分文件、未保存零件、装配体或组件；不会自动删除、关闭或回滚。请勿用于正式工程。是否继续？",
                        "多段空装配体试验", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) != MessageBoxResult.Yes)
                    {
                        _result.Text = "已取消多段装配试验，未创建模型或保存零件文件。";
                        SolidWorksFileLogger.Info("用户取消多段空装配体试验。");
                        return;
                    }
                    RequireSourceDocument();
                    PipelineRoutePickingWindow.EnsureReadyToPick(_application, _sourceDocument);
                    PipelineMultiPieceAssemblyTrialResult assembly = PipelineMultiPieceAssemblyTrial.WithFreshSave(() =>
                    {
                        PipelineMultiPieceSavedResult saved = PipelineMultiPieceSaveTrial.BuildAndSave(
                            _application, _sourceDocument, _mainWindow, partTemplate, plan!, confirmation);
                        savedPaths = paths;
                        return PipelineMultiPieceAssemblyTrial.Build(
                            _application, _sourceDocument, _mainWindow, assemblyTemplate, saved);
                    });
                    _result.Text = "多段零件已插入新建未保存的测试装配体；未主动重建、自动保存或添加配合，未校验连接或干涉。";
                    MessageBox.Show(this, _result.Text + "\n装配体：" + assembly.AssemblyTitle +
                        "\n零件文件（按路线顺序）：\n" + string.Join("\n", paths) +
                        "\n组件可能固定或浮动；未写入属性或数据库。请检查测试装配体。",
                        "多段空装配体试验", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (PipelineMultiPieceSaveTrialException ex)
            {
                SolidWorksFileLogger.Error("多段装配试验的保存阶段未完成：" + ex);
                _result.Text = "多段装配试验未完成：" + ex.Message;
                MessageBox.Show(this, _result.Text, "多段空装配体试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (PipelineMultiPieceAssemblyTrialException ex)
            {
                SolidWorksFileLogger.Error("多段装配试验未完成：" + ex);
                _result.Text = "多段装配试验未完成：" + ex.Message;
                MessageBox.Show(this, _result.Text, "多段空装配体试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException ||
                ex is ArithmeticException || ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException ||
                ex is System.Runtime.InteropServices.InvalidComObjectException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                SolidWorksFileLogger.Error("多段装配试验未启动或未完成；异常类型=" + ex.GetType().Name + "。");
                _result.Text = "多段装配试验未完成：" + ex.Message;
                string state = savedPaths == null ? "\n请检查是否有文件或文档残留。" :
                    "\n零件此前已保存到：\n" + string.Join("\n", savedPaths);
                MessageBox.Show(this, _result.Text + state + "\n未自动删除、关闭或回滚。",
                    "多段空装配体试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { SetTrialRunning(false); }
        }

        private bool CanAttemptExistingAssembly()
        {
            return ExistingAssemblyTrialEnabled && _application != null && _sourceDocument != null && _routeGuard != null &&
                _routeGuard.IsValid && _canAttemptInsertion != null && _canAttemptInsertion() &&
                _markInsertionAttempted != null;
        }

        private void AssembleIntoExisting()
        {
            if (_building || !CanAttemptExistingAssembly())
            {
                ShowError("当前路线的装配体上下文不可用或已尝试插入，请重新选点。");
                return;
            }
            if (!TryCreatePlan(true, out PipelineMultiSegmentPlan? plan, out _)) return;
            SetTrialRunning(true);
            IReadOnlyList<string>? savedPaths = null;
            try
            {
                RequireSourceDocument();
                PipelineRoutePickingWindow.EnsureReadyToPick(_application!, _sourceDocument!);
                _routeGuard!.Observe();
                string? configuration = _routeGuard.Configuration;
                if (!_routeGuard.IsValid || configuration == null || string.IsNullOrWhiteSpace(configuration))
                    throw new InvalidOperationException("无法确认拾点始终位于同一装配体根级及配置，请重新选点。");
                var baseline = PipelineStraightAssemblyTrial.CaptureMultipleComponentTarget(
                    _application!, _sourceDocument!, configuration, _mainWindow);
                string title = PipelineStraightAssemblyTrial.ValidateTarget(
                    _application!, _sourceDocument!, configuration, _mainWindow, baseline);
                string? template = ChoosePartTemplate();
                if (template == null)
                {
                    _result.Text = "已取消选择零件模板，未创建或保存零件。";
                    SolidWorksFileLogger.Info("用户取消双零件装配体多段试验的零件模板选择。");
                    return;
                }
                string? folder = ChooseEmptyTestFolder();
                if (folder == null)
                {
                    _result.Text = "已取消选择测试文件夹，未创建或保存零件。";
                    SolidWorksFileLogger.Info("用户取消双零件装配体多段试验的文件夹选择。");
                    return;
                }
                using (PipelineMultiPieceSaveConfirmation confirmation =
                    PipelineMultiPieceSaveTrial.PreparePaths(_application!, _mainWindow, folder, plan!))
                {
                    IReadOnlyList<string> paths = confirmation.Paths;
                    if (MessageBox.Show(this, "仅用于您专门新建的未保存测试装配体；必须恰有两个已保存、固定、无配合的根级测试零件，严禁用于正式工程。\n" +
                        "拾点后请勿移动或修改已有组件；本试验只能检查开始时捕获的基线，无法证明拾点后至捕获前未发生变化。\n" +
                        "将重新创建并保存 " + paths.Count + " 个独立测试零件：\n" + string.Join("\n", paths) +
                        "\n随后向原测试装配体逐件插入；必要时只调整新增组件的位置，新组件应保持浮动。不会自动保存、重建、添加配合或写入数据库，也不检查连接及干涉。\n" +
                        "试验期间请勿让其他程序或人员访问或更改文件及目录；检查不是事务，不能防止所有并发变化。失败或异常退出可能留下文件、文档或组件，不自动删除、关闭或回滚。\n" +
                        "本次路线一旦开始创建零件将锁定重复插入；是否继续？",
                        "双零件装配体多段试验", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) != MessageBoxResult.Yes)
                    {
                        _result.Text = "已取消双零件装配体多段试验，未创建模型或保存零件文件。";
                        SolidWorksFileLogger.Info("用户取消双零件装配体多段试验。");
                        return;
                    }
                    RequireSourceDocument();
                    PipelineRoutePickingWindow.EnsureReadyToPick(_application!, _sourceDocument!);
                    _routeGuard.Observe();
                    if (!CanAttemptExistingAssembly() ||
                        !string.Equals(_routeGuard.Configuration, configuration, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(PipelineStraightAssemblyTrial.ValidateTarget(
                            _application!, _sourceDocument!, configuration, _mainWindow, baseline), title, StringComparison.Ordinal))
                        throw new InvalidOperationException("路线或目标装配体上下文已变化，未创建零件。");

                    // 创建开始即锁定本次路线，避免失败或未知残留后重复插入。
                    _markInsertionAttempted!();
                    PipelineMultiPieceAssemblyTrial.WithFreshSave(() =>
                    {
                        PipelineMultiPieceSavedResult saved = PipelineMultiPieceSaveTrial.BuildAndSave(
                            _application!, _sourceDocument!, _mainWindow, template, plan!, confirmation);
                        savedPaths = paths;
                        PipelineMultiPieceExistingAssemblyTrial.Build(_application!, _sourceDocument!, _mainWindow,
                            title, configuration, baseline, saved);
                        return 0;
                    });
                    _result.Text = "多段零件已插入双零件测试装配体；未自动保存、重建、添加配合或检查连接及干涉。";
                    MessageBox.Show(this, _result.Text + "\n零件文件（按路线顺序）：\n" + string.Join("\n", paths) +
                        "\n新组件保持浮动；请检查原有组件和新增组件。", "双零件装配体多段试验",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex) when (ex is PipelineMultiPieceSaveTrialException ||
                ex is PipelineMultiPieceExistingAssemblyTrialException || ex is InvalidOperationException ||
                ex is ArgumentException || ex is ArithmeticException || ex is NotSupportedException ||
                ex is System.Runtime.InteropServices.COMException || ex is System.Runtime.InteropServices.InvalidComObjectException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                SolidWorksFileLogger.Error("双零件装配体多段试验未完成；异常类型=" + ex.GetType().Name + "；未自动回滚。");
                _result.Text = "双零件装配体多段试验未完成：" + ex.Message;
                string state = savedPaths == null ? "\n请检查是否有文件或文档残留。" :
                    "\n零件此前已保存到：\n" + string.Join("\n", savedPaths);
                MessageBox.Show(this, _result.Text + state + "\n未自动删除、关闭或回滚。",
                    "双零件装配体多段试验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { SetTrialRunning(false); }
        }

        private string? ChooseAssemblyTemplate()
        {
            string template = _application!.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            if (!string.IsNullOrWhiteSpace(template) && PipelineStraightModelBuilder.IsFullyQualifiedPath(template) &&
                File.Exists(template) && string.Equals(Path.GetExtension(template), ".asmdot", StringComparison.OrdinalIgnoreCase))
                return template;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择用于多段空装配体试验的装配体模板",
                Filter = "SolidWorks 装配体模板 (*.asmdot)|*.asmdot",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return null;
            if (!PipelineStraightModelBuilder.IsFullyQualifiedPath(dialog.FileName) || !File.Exists(dialog.FileName) ||
                !string.Equals(Path.GetExtension(dialog.FileName), ".asmdot", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("必须选择存在的装配体模板 .asmdot 绝对路径。");
            return dialog.FileName;
        }

        private string? ChooseEmptyTestFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "请选择专用的空测试文件夹；仅支持本机固定 NTFS 磁盘。",
                ShowNewFolderButton = true
            })
                return dialog.ShowDialog(new FormsWindowOwner(new WindowInteropHelper(this).Handle)) ==
                    System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        private sealed class FormsWindowOwner : System.Windows.Forms.IWin32Window
        {
            internal FormsWindowOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }

        private void RequireSourceDocument()
        {
            var active = _application!.ActiveDoc as ModelDoc2;
            if (active == null || _application.IsSame(active, _sourceDocument) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("原路线文档不是活动文档，请切回原文档后重新打开试算窗口。");
        }

        private void SetTrialRunning(bool running)
        {
            _building = running;
            _radius.IsEnabled = !running;
            _calculate.IsEnabled = !running;
            _build.IsEnabled = !running && _application != null && _sourceDocument != null;
            _save.IsEnabled = !running && _application != null && _sourceDocument != null;
            _assembly.IsEnabled = !running && _application != null && _sourceDocument != null;
            _existingAssembly.IsEnabled = !running && CanAttemptExistingAssembly();
            _close.IsEnabled = !running;
        }

        private void PreventCloseDuringBuild(object? sender, CancelEventArgs args)
        {
            if (!_building) return;
            args.Cancel = true;
            _result.Text = "建模试验尚未结束，请等待完成后再关闭窗口。";
            SolidWorksFileLogger.Warning("多段独立零件试验尚未结束，暂不能关闭试算窗口。");
        }

        private void ShowError(string message)
        {
            _result.Text = "多段管道操作未完成：" + message;
            SolidWorksFileLogger.Warning("多段管道操作未完成：" + message);
            MessageBox.Show(this, message, "多段管道几何试算", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static double Length(PipePoint3D start, PipePoint3D end)
        {
            double x = end.X - start.X, y = end.Y - start.Y, z = end.Z - start.Z;
            double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if (scale == 0 || double.IsInfinity(scale) || double.IsNaN(scale))
                throw new ArithmeticException("直管长度无法计算。");
            double length = scale * Math.Sqrt((x / scale) * (x / scale) + (y / scale) * (y / scale) + (z / scale) * (z / scale));
            if (double.IsInfinity(length) || double.IsNaN(length))
                throw new ArithmeticException("直管长度溢出。");
            return length;
        }

        private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

        private static string Describe(PipePoint3D point) =>
            "X=" + Format(point.X) + "  Y=" + Format(point.Y) + "  Z=" + Format(point.Z);
    }
}

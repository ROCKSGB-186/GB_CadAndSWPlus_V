using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>路线拾点试运行窗口；完成后保留坐标供查看，不创建或保存模型。</summary>
    internal sealed class PipelineRoutePickingWindow : Window
    {
        private readonly SldWorks _application;
        private readonly ModelDoc2 _document;
        private readonly ComboBox _plane;
        private readonly TextBox _fixedCoordinate;
        private readonly TextBlock _cursor;
        private readonly TextBlock _status;
        private readonly TextBox _points;
        private readonly Button _applyPlane;
        private readonly Button _undo;
        private readonly Button _edit;
        private readonly Button _complete;
        private readonly Button _resume;
        private readonly Button _properties;
        private PipelineRoutePickingSession? _session;
        private PipelineRouteKeyboardHook? _keyboardHook;
        private PipelineRouteSelectionSession? _suspendedSelection;
        private bool _selecting;
        private bool _forceClose;
        private bool _hasDisplayedPoints;
        private bool _loadingProperties;
        private bool _windowClosed;
        private PipeRoute? _completedRoute;
        private IDictionary<string, string>? _confirmedAttributes;
        private CancellationTokenSource? _catalogCancellation;

        public PipelineRoutePickingWindow(SldWorks application, ModelDoc2 document, IntPtr owner)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _document = document ?? throw new ArgumentNullException(nameof(document));
            if (owner == IntPtr.Zero) throw new ArgumentException("SolidWorks 主窗口句柄无效。", nameof(owner));
            new WindowInteropHelper(this).Owner = owner;
            Title = "绘制管道——路线选点试运行";
            Width = 480;
            Height = 470;
            MinWidth = 420;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(12) };
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock
            {
                Text = "当前仅验证路线选点和属性，不生成或保存管道模型；关闭窗口后路线和本次确认属性不会保存。",
                Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            heading.Children.Add(new TextBlock
            {
                Text = "左键拾点并确认坐标；视图操作暂沿用 SolidWorks 原有方式。",
                TextWrapping = TextWrapping.Wrap
            });
            heading.Children.Add(new TextBlock
            {
                Text = "先将焦点切回图形区（如轻按中键）：Z 撤销、X 编辑、空格完成、Esc 取消；输入框和对话框不接管快捷键。",
                TextWrapping = TextWrapping.Wrap
            });
            var planeRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            planeRow.Children.Add(new TextBlock { Text = "工作平面：", VerticalAlignment = VerticalAlignment.Center });
            _plane = new ComboBox { Width = 68, Margin = new Thickness(0, 0, 8, 0) };
            _plane.ItemsSource = new[] { PipeWorkPlaneKind.XY, PipeWorkPlaneKind.XZ, PipeWorkPlaneKind.YZ };
            _plane.SelectedIndex = 0;
            planeRow.Children.Add(_plane);
            planeRow.Children.Add(new TextBlock { Text = "固定坐标（mm）：", VerticalAlignment = VerticalAlignment.Center });
            _fixedCoordinate = new TextBox { Width = 85, Text = "0", Margin = new Thickness(0, 0, 8, 0) };
            planeRow.Children.Add(_fixedCoordinate);
            _applyPlane = new Button { Content = "应用平面", Padding = new Thickness(6, 2, 6, 2) };
            _applyPlane.Click += (sender, args) => ApplyPlane();
            planeRow.Children.Add(_applyPlane);
            heading.Children.Add(planeRow);
            heading.Children.Add(new TextBlock
            {
                Text = "XY 固定 Z；XZ 固定 Y；YZ 固定 X。确认坐标后，平面自动跟随该点。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4)
            });
            _cursor = new TextBlock { Text = "请将鼠标移到图形区。", TextWrapping = TextWrapping.Wrap };
            _status = new TextBlock { Text = "准备开始路线选点。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
            heading.Children.Add(_cursor);
            heading.Children.Add(_status);
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);

            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            _undo = AddButton(actions, "撤销最后点", (sender, args) => Undo());
            _edit = AddButton(actions, "编辑最后点", (sender, args) => Edit());
            _complete = AddButton(actions, "完成点列", (sender, args) => Complete());
            _resume = AddButton(actions, "恢复选点", (sender, args) => Resume());
            _resume.IsEnabled = false;
            _properties = AddButton(actions, "管道属性", (sender, args) => OpenProperties());
            _properties.IsEnabled = false;
            AddButton(actions, "关闭", (sender, args) => Close());
            DockPanel.SetDock(actions, Dock.Bottom);
            root.Children.Add(actions);
            _points = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = "尚未确认坐标。"
            };
            root.Children.Add(_points);
            Content = root;
            Closing += OnClosing;
            Closed += (sender, args) =>
            {
                _windowClosed = true;
                _catalogCancellation?.Cancel();
            };
        }

        public bool Start()
        {
            try
            {
                EnsureReadyToPick(_application, _document);
                _session = new PipelineRoutePickingSession(_application, _document, ConfirmPoint,
                    ShowCursor, ShowSelection, ShowMessage, OnUnexpectedStop);
                _session.Start();
                _selecting = true;
                SetControlsEnabled(true);
                _status.Text = "路线选点已启动。";
                StartKeyboardHook();
                SolidWorksFileLogger.Info("已打开绘管路线选点试运行窗口。");
                return true;
            }
            catch (Exception ex) when (IsOperationException(ex))
            {
                _selecting = false;
                SetControlsEnabled(false);
                try { ReleaseInputResources(); }
                catch (Exception cleanupError) when (IsOperationException(cleanupError))
                {
                    ReportError("启动路线选点失败且清理未完成，请关闭窗口重试", new AggregateException(ex, cleanupError));
                    return false;
                }
                ReportError("启动路线选点失败", ex);
                return false;
            }
        }

        /// <summary>插件断开时先清理事件，再由用户决定是否复制尚未保存的坐标。</summary>
        public bool CloseForShutdown(string reason)
        {
            SolidWorksFileLogger.Info("结束路线选点窗口；原因=" + reason + "。");
            _selecting = false;
            SetControlsEnabled(false);
            try
            {
                ReleaseInputResources();
            }
            catch (Exception ex) when (IsOperationException(ex))
            {
                _selecting = false;
                SetControlsEnabled(false);
                ReportError("清理路线选点失败，请再次关闭窗口重试", ex);
                return false;
            }
            if (_hasDisplayedPoints &&
                MessageBox.Show(this, "插件正在断开，关闭窗口后路线坐标不会保存。是否先复制坐标到剪贴板？\n选择“否”将放弃这些坐标。",
                    "保存路线坐标", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                while (true)
                {
                    try
                    {
                        Clipboard.SetText(_points.Text);
                        SolidWorksFileLogger.Info("插件断开前已将路线坐标复制到剪贴板。");
                        break;
                    }
                    catch (ExternalException ex)
                    {
                        SolidWorksFileLogger.Error("插件断开前复制路线坐标失败：" + ex);
                        if (MessageBox.Show(this, "复制失败，请关闭可能占用剪贴板的程序后重试。\n" +
                            "选择“是”重试；选择“否”将放弃路线坐标并继续关闭。", "路线坐标复制失败",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                            continue;
                        SolidWorksFileLogger.Warning("用户放弃复制未保存的路线坐标。");
                        break;
                    }
                }
            }
            _forceClose = true;
            try { Close(); }
            finally { _forceClose = false; }
            return true;
        }

        private PipePoint3D? ConfirmPoint(PipePoint3D point, PipePoint3D? previous)
        {
            var window = new PipelinePointConfirmWindow(point, previous, false) { Owner = this };
            return window.ShowDialog() == true ? window.ConfirmedPoint : (PipePoint3D?)null;
        }

        private void ShowCursor(PipePoint3D? point)
        {
            _cursor.Text = point.HasValue ? "动态坐标（mm）：" + Describe(point.Value) : "请将鼠标移到图形区。";
        }

        private void ShowSelection(IReadOnlyList<PipePoint3D> points, PipeWorkPlane plane)
        {
            _plane.SelectedItem = plane.Kind;
            _fixedCoordinate.Text = plane.FixedCoordinateMm.ToString("G17", CultureInfo.InvariantCulture);
            var text = new StringBuilder();
            for (int i = 0; i < points.Count; i++)
                text.Append(i + 1).Append(". ").AppendLine(Describe(points[i]));
            _points.Text = points.Count == 0 ? "尚未确认坐标。" : text.ToString();
            _hasDisplayedPoints = points.Count > 0;
            _status.Text = "已确认 " + points.Count + " 个坐标；工作平面：" + plane.Kind + "。";
        }

        private void ShowMessage(string message) { _status.Text = message; }

        private void OnUnexpectedStop(string reason)
        {
            _selecting = false;
            SetControlsEnabled(false);
            try { StopKeyboardHook(); }
            catch (Exception ex) when (IsOperationException(ex))
            {
                _resume.IsEnabled = _session?.HasPausedSelection == true;
                ReportError("路线已停止，但快捷键监听清理失败，请重试", ex);
                return;
            }
            if (_session?.HasPausedSelection == true)
            {
                _resume.IsEnabled = true;
                _status.Text = reason + " 红线暂时隐藏，已确认坐标仍保留。";
                return;
            }
            _resume.IsEnabled = false;
            _status.Text = "选点已停止：" + reason;
            MessageBox.Show(this, reason, "路线选点已停止", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Resume()
        {
            try
            {
                if (_session == null) throw new InvalidOperationException("没有可恢复的路线选点会话。");
                // 旧事件必须完全释放；快照单独保留，恢复失败时不丢失已选点。
                StopKeyboardHook();
                _session.Dispose();
                if (_suspendedSelection == null)
                    _suspendedSelection = _session.CopyPausedSelection()
                        ?? throw new InvalidOperationException("路线暂停数据不可用，无法恢复选点。");
                EnsureReadyToPick(_application, _document);
                var next = new PipelineRoutePickingSession(_application, _document, ConfirmPoint,
                    ShowCursor, ShowSelection, ShowMessage, OnUnexpectedStop, _suspendedSelection);
                _session = next;
                next.Start();
                _selecting = true;
                _resume.IsEnabled = false;
                SetControlsEnabled(true);
                ShowSelection(next.Points, next.WorkPlane);
                _status.Text = "路线选点已恢复；已确认坐标和工作平面保持不变。";
                StartKeyboardHook();
                _suspendedSelection = null;
                SolidWorksFileLogger.Info("已恢复 SolidWorks 路线选点；点数=" + next.Points.Count + "。");
            }
            catch (Exception ex) when (IsOperationException(ex))
            {
                _selecting = false;
                SetControlsEnabled(false);
                _resume.IsEnabled = true;
                try { ReleaseInputResources(); }
                catch (Exception cleanupError) when (IsOperationException(cleanupError))
                {
                    ReportError("恢复路线选点失败且清理未完成，请重试", new AggregateException(ex, cleanupError));
                    return;
                }
                ReportError("恢复路线选点失败，请结束原生命令并检查原文档后重试", ex);
            }
        }

        internal static void EnsureReadyToPick(SldWorks application, ModelDoc2 document)
        {
            var active = application.ActiveDoc as ModelDoc2;
            if (active == null || application.IsSame(active, document) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("请切回开始选点时的 SolidWorks 文档。");
            if (document.SketchManager?.ActiveSketch != null)
                throw new InvalidOperationException("请先退出当前草图，再启动或恢复路线选点。");
            application.GetRunningCommandInfo(out int command, out string title, out bool commandUiActive);
            if (commandUiActive)
                throw new InvalidOperationException("SolidWorks 当前有活动命令或属性管理器，请先结束后再启动或恢复路线选点。");
        }

        private void ApplyPlane()
        {
            if (!TryReadCoordinate(_fixedCoordinate.Text, out double value))
            {
                _status.Text = "请输入有效的有限固定坐标，单位为毫米。";
                SolidWorksFileLogger.Warning("设置路线工作平面失败：固定坐标无效。");
                return;
            }
            Execute("设置工作平面", () =>
            {
                if (!_session!.TrySetWorkPlane(new PipeWorkPlane((PipeWorkPlaneKind)_plane.SelectedItem, value), out string error))
                    _status.Text = error;
            });
        }

        private void Undo()
        {
            Execute("撤销路线坐标", () =>
            {
                if (!_session!.TryRemoveLastPoint(out string error)) _status.Text = error;
            });
        }

        private void Edit()
        {
            Execute("编辑路线坐标", () =>
            {
                if (!_session!.TryEditLastPoint(out string error)) _status.Text = error;
            });
        }

        private void Complete()
        {
            Execute("完成路线点列", () =>
            {
                if (!_session!.TryComplete(out PipeRoute? route, out string error))
                {
                    _status.Text = error;
                    return;
                }
                _selecting = false;
                SetControlsEnabled(false);
                StopKeyboardHook();
                _completedRoute = route!;
                _properties.IsEnabled = true;
                _status.Text = "路线点列已完成；准备打开属性试运行页面。";
                OpenProperties();
            });
        }

        private async void OpenProperties()
        {
            if (_loadingProperties || _completedRoute == null || _windowClosed)
            {
                SolidWorksFileLogger.Warning("无法打开绘管属性：路线未完成或属性页面正在打开。");
                return;
            }
            _loadingProperties = true;
            _properties.IsEnabled = false;
            _status.Text = "正在读取共享管道字段目录，请稍候。";
            try
            {
                IReadOnlyList<PipelinePropertiesFieldDefinition>? fields = null;
                bool offline = false;
                using (var cancellation = new CancellationTokenSource())
                {
                    _catalogCancellation = cancellation;
                    try
                    {
                        var session = new SharedLoginSessionStore().Load();
                        fields = await new PipelineFieldCatalogService().GetFieldsAsync(session, cancellation.Token);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        SolidWorksFileLogger.Info("路线窗口已关闭，取消读取管道字段目录。");
                        return;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException ||
                                               ex is InvalidOperationException || ex is ArgumentException)
                    {
                        offline = true;
                        SolidWorksFileLogger.Warning("共享管道字段目录读取失败，将提示使用本地精简字段；异常类型=" +
                            ex.GetType().Name + "。");
                    }
                    finally
                    {
                        _catalogCancellation = null;
                    }
                }
                if (_windowClosed) return;
                if (offline)
                {
                    MessageBox.Show(this, "无法读取服务器共享管道字段目录，将使用本地精简字段。当前仅为属性试运行，不会生成模型。",
                        "管道字段目录", MessageBoxButton.OK, MessageBoxImage.Warning);
                    if (_windowClosed) return;
                }
                var window = new PipelinePropertiesWindow(fields, _confirmedAttributes, null, true) { Owner = this };
                bool? result = window.ShowDialog();
                if (_windowClosed) return;
                if (result == true)
                {
                    _confirmedAttributes = new Dictionary<string, string>(window.ConfirmedAttributes, StringComparer.OrdinalIgnoreCase);
                    _status.Text = "属性已确认，仅保存在本次窗口中；未生成模型，也未自动保存配置。";
                    SolidWorksFileLogger.Info("试运行管道属性已确认；未生成模型或自动保存配置。");
                    MessageBox.Show(this, "属性已确认。本阶段不会生成模型，也不会自动保存配置；关闭路线窗口后本次属性将丢失。",
                        "管道属性试运行", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _status.Text = "已取消本次属性编辑；如曾点击“保存配置”，该配置仍然保留。";
                    SolidWorksFileLogger.Info("用户取消试运行管道属性编辑。");
                }
            }
            catch (Exception ex) when (IsOperationException(ex) || ex is HttpRequestException ||
                                       ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                if (!_windowClosed) ReportError("打开管道属性失败，可重试", ex);
                else SolidWorksFileLogger.Error("路线窗口关闭后属性处理失败；异常类型=" + ex.GetType().Name + "。");
            }
            finally
            {
                _loadingProperties = false;
                if (!_windowClosed) _properties.IsEnabled = _completedRoute != null;
            }
        }

        private void OnClosing(object? sender, CancelEventArgs args)
        {
            if (!_forceClose && _hasDisplayedPoints &&
                MessageBox.Show(this, "关闭后当前路线坐标不会保存，是否关闭？", "关闭路线选点",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                args.Cancel = true;
                return;
            }
            _selecting = false;
            SetControlsEnabled(false);
            try
            {
                ReleaseInputResources();
                SolidWorksFileLogger.Info("已关闭路线选点窗口。");
            }
            catch (Exception ex) when (IsOperationException(ex))
            {
                args.Cancel = true;
                _selecting = false;
                SetControlsEnabled(false);
                ReportError("清理路线选点失败，请再次关闭窗口重试", ex);
            }
        }

        private void Execute(string operation, Action action)
        {
            if (!_selecting || _session == null)
            {
                _status.Text = "路线选点未运行。";
                return;
            }
            try { action(); }
            catch (Exception ex) when (IsOperationException(ex))
            {
                _selecting = false;
                SetControlsEnabled(false);
                bool paused = _session.HasPausedSelection;
                try { ReleaseInputResources(); }
                catch (Exception cleanupError) when (IsOperationException(cleanupError))
                {
                    _resume.IsEnabled = paused;
                    ReportError(operation + "失败，清理未完成，请关闭窗口重试", new AggregateException(ex, cleanupError));
                    return;
                }
                if (paused)
                {
                    OnUnexpectedStop("检测到 SolidWorks 新命令，路线选点已暂停；请结束该命令后点击“恢复选点”。");
                    return;
                }
                ReportError(operation + "失败，路线选点已停止", ex);
            }
        }

        private void ReportError(string operation, Exception ex)
        {
            SolidWorksFileLogger.Error(operation + "：" + ex);
            _status.Text = operation + "：" + ex.Message;
            MessageBox.Show(this, _status.Text, "路线选点", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void StartKeyboardHook()
        {
            if (_session == null) throw new InvalidOperationException("路线选点尚未启动。");
            PipelineRouteKeyboardHook? hook = null;
            hook = new PipelineRouteKeyboardHook(_session.ViewWindow, new WindowInteropHelper(this).Owner,
                () => _selecting && _session != null && _session.CanHandleShortcut,
                OnShortcut, error => OnShortcutError(hook!, error));
            _keyboardHook = hook;
            try
            {
                hook.Start();
                SolidWorksFileLogger.Info("已启用 SolidWorks 路线选点快捷键。");
            }
            catch (Exception ex) when (IsOperationException(ex))
            {
                StopKeyboardHook();
                SolidWorksFileLogger.Warning("路线快捷键启用失败，保留窗口按钮操作：" + ex.Message);
                _status.Text = "快捷键不可用，请使用窗口按钮：" + ex.Message;
                MessageBox.Show(this, _status.Text, "路线快捷键", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StopKeyboardHook()
        {
            if (_keyboardHook == null) return;
            _keyboardHook.Dispose();
            _keyboardHook = null;
            SolidWorksFileLogger.Info("已释放 SolidWorks 路线选点快捷键。");
        }

        private void ReleaseInputResources()
        {
            var failures = new List<Exception>();
            try { StopKeyboardHook(); }
            catch (Exception ex) when (IsOperationException(ex)) { failures.Add(ex); }
            try { _session?.Dispose(); }
            catch (Exception ex) when (IsOperationException(ex)) { failures.Add(ex); }
            if (failures.Count > 0) throw new AggregateException("路线输入资源清理失败。", failures);
        }

        private void OnShortcut(PipelineRouteShortcut shortcut)
        {
            switch (shortcut)
            {
                case PipelineRouteShortcut.Undo:
                    SolidWorksFileLogger.Info("执行路线选点快捷键：撤销最后点。"); Undo(); break;
                case PipelineRouteShortcut.Edit:
                    SolidWorksFileLogger.Info("执行路线选点快捷键：编辑最后点。"); Edit(); break;
                case PipelineRouteShortcut.Complete:
                    SolidWorksFileLogger.Info("执行路线选点快捷键：完成点列。"); Complete(); break;
                case PipelineRouteShortcut.Cancel:
                    SolidWorksFileLogger.Info("执行路线选点快捷键：取消选点。"); Close(); break;
                default: throw new InvalidOperationException("未知路线选点快捷键。");
            }
        }

        private void OnShortcutError(PipelineRouteKeyboardHook hook, Exception error)
        {
            if (_keyboardHook != hook)
            {
                SolidWorksFileLogger.Warning("已结束的路线快捷键监听收到延迟错误：" + error.Message);
                return;
            }
            try { StopKeyboardHook(); }
            catch (Exception cleanupError) when (IsOperationException(cleanupError))
            {
                ReportError("路线快捷键失败且未能释放，请关闭窗口重试", new AggregateException(error, cleanupError));
                return;
            }
            ReportError("路线快捷键不可用，可继续使用窗口按钮", error);
        }

        private void SetControlsEnabled(bool enabled)
        {
            _plane.IsEnabled = enabled;
            _fixedCoordinate.IsEnabled = enabled;
            _applyPlane.IsEnabled = enabled;
            _undo.IsEnabled = enabled;
            _edit.IsEnabled = enabled;
            _complete.IsEnabled = enabled;
        }

        private static Button AddButton(Panel panel, string text, RoutedEventHandler handler)
        {
            var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(7, 3, 7, 3) };
            button.Click += handler;
            panel.Children.Add(button);
            return button;
        }

        private static bool TryReadCoordinate(string text, out double value)
        {
            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            return parsed && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string Describe(PipePoint3D point) =>
            "X=" + point.X.ToString("G17", CultureInfo.InvariantCulture) +
            "  Y=" + point.Y.ToString("G17", CultureInfo.InvariantCulture) +
            "  Z=" + point.Z.ToString("G17", CultureInfo.InvariantCulture);

        private static bool IsOperationException(Exception ex) =>
            ex is InvalidOperationException || ex is ArgumentException || ex is AggregateException ||
            ex is COMException || ex is InvalidComObjectException || ex is Win32Exception ||
            ex is NotSupportedException || ex is ArithmeticException ||
            ex is DllNotFoundException || ex is EntryPointNotFoundException;
    }
}

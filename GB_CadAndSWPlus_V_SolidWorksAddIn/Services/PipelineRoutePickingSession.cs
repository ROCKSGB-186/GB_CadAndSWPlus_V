using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// 连接鼠标拾点、自由端路线和临时预览；键盘及窗口由调用方管理。
    /// 所有回调均在 SolidWorks 界面线程执行；动态状态回调不应打开模态窗口。
    /// </summary>
    internal sealed class PipelineRoutePickingSession : IDisposable
    {
        private readonly SldWorks _application;
        private readonly ModelDoc2 _document;
        private readonly ModelView _view;
        private readonly SolidWorks.Interop.sldworks.Mouse _mouse;
        private readonly DMouseEvents_Event _mouseEvents;
        private readonly DSldWorksEvents_Event _applicationEvents;
        private readonly DSldWorksEvents_CommandOpenPreNotifyEventHandler _commandOpening;
        private readonly DMouseEvents_MouseMoveNotifyEventHandler _mouseMove;
        private readonly DMouseEvents_MouseLBtnDownNotifyEventHandler _mouseDown;
        private readonly PipelineRouteSelectionSession _selection;
        private readonly PipelineRoutePreviewSession _preview;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _moveTimer;
        private readonly int _managedThreadId;
        private readonly IntPtr _viewWindow;
        private readonly Func<PipePoint3D, PipePoint3D?, PipePoint3D?> _confirmPoint;
        private readonly Action<PipePoint3D?> _cursorChanged;
        private readonly Action<IReadOnlyList<PipePoint3D>, PipeWorkPlane> _selectionChanged;
        private readonly Action<string> _message;
        private readonly Action<string> _unexpectedStop;

        private bool _moveSubscribed;
        private bool _downSubscribed;
        private bool _commandSubscribed;
        private bool _startAttempted;
        private bool _started;
        private bool _stopping;
        private bool _disposed;
        private bool _confirming;
        private bool _pendingMove;
        private bool _pendingPreviewRefresh;
        private int _latestX;
        private int _latestY;
        private PipePoint3D? _candidate;
        private PipelineRouteSelectionSession? _pausedSelection;
        private string _lastMessage = string.Empty;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

        public PipelineRoutePickingSession(
            SldWorks application, ModelDoc2 document,
            Func<PipePoint3D, PipePoint3D?, PipePoint3D?> confirmPoint,
            Action<PipePoint3D?> cursorChanged,
            Action<IReadOnlyList<PipePoint3D>, PipeWorkPlane> selectionChanged,
            Action<string> message, Action<string> unexpectedStop,
            PipelineRouteSelectionSession? previousSelection = null)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _confirmPoint = confirmPoint ?? throw new ArgumentNullException(nameof(confirmPoint));
            _cursorChanged = cursorChanged ?? throw new ArgumentNullException(nameof(cursorChanged));
            _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _unexpectedStop = unexpectedStop ?? throw new ArgumentNullException(nameof(unexpectedStop));
            _selection = previousSelection?.CopySelecting() ?? new PipelineRouteSelectionSession();
            _preview = new PipelineRoutePreviewSession(application, document, OnPreviewStopped);
            _managedThreadId = Thread.CurrentThread.ManagedThreadId;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _view = document.ActiveView as ModelView ??
                throw new InvalidOperationException("无法获取路线拾点视图。");
            _viewWindow = new IntPtr(_view.GetViewHWndx64());
            if (_viewWindow == IntPtr.Zero) throw new InvalidOperationException("无法获取路线拾点视图窗口。");
            _mouse = _view.GetMouse() ?? throw new InvalidOperationException("无法获取路线拾点鼠标事件对象。");
            _mouseEvents = _mouse as DMouseEvents_Event ??
                throw new InvalidOperationException("无法订阅路线拾点鼠标事件。");
            _applicationEvents = application as DSldWorksEvents_Event ??
                throw new InvalidOperationException("无法订阅 SolidWorks 命令事件。");
            _commandOpening = OnCommandOpening;
            _mouseMove = OnMouseMove;
            _mouseDown = OnMouseDown;
            _moveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _moveTimer.Tick += OnMoveTick;
        }

        public IReadOnlyList<PipePoint3D> Points => _selection.Points;
        public PipeWorkPlane WorkPlane => _selection.WorkPlane;

        /// <summary>启动预览和鼠标事件；失败时保留实例供调用方重试清理。</summary>
        public void Start()
        {
            RequireUiThread();
            if (_startAttempted || _disposed || _stopping)
                throw new InvalidOperationException("路线拾点会话已经启动或结束。");
            _startAttempted = true;
            try
            {
                _preview.Start();
                if (!IsCurrent()) throw new InvalidOperationException("活动文档或视图已切换，无法开始拾点。");
                _mouseEvents.MouseMoveNotify += _mouseMove;
                _moveSubscribed = true;
                _mouseEvents.MouseLBtnDownNotify += _mouseDown;
                _downSubscribed = true;
                _applicationEvents.CommandOpenPreNotify += _commandOpening;
                _commandSubscribed = true;
                if (!IsCurrent()) throw new InvalidOperationException("订阅鼠标事件时活动文档或视图已切换。");
                _started = true;
                _moveTimer.Start();
                _view.Activate();
                if (_selection.Points.Count > 0) RefreshPreview();
            }
            catch (Exception ex) when (IsInputException(ex))
            {
                try { DisposeCore(); }
                catch (AggregateException cleanupError)
                {
                    throw new AggregateException("启动路线拾点失败，且清理未完成。", ex, cleanupError);
                }
                throw;
            }
            SolidWorksFileLogger.Info("已启动 SolidWorks 路线鼠标拾点会话。");
        }

        public bool TrySetWorkPlane(PipeWorkPlane plane, out string error)
        {
            RequireAvailable();
            if (!_selection.TrySetWorkPlane(plane, out error)) return false;
            try
            {
                ClearCandidate();
                RefreshPreview();
                NotifySelectionChanged();
            }
            catch (Exception ex) when (IsInputException(ex))
            {
                RequestStop("设置工作平面后的状态更新失败，拾点已停止：" + ex.Message);
                throw;
            }
            SolidWorksFileLogger.Info("已设置路线工作平面；类型=" + plane.Kind +
                "；固定坐标（毫米）=" + plane.FixedCoordinateMm.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "。");
            return true;
        }

        public bool TryRemoveLastPoint(out string error)
        {
            RequireAvailable();
            if (!_selection.TryRemoveLastPoint(out error)) return false;
            try
            {
                ClearCandidate();
                RefreshPreview();
                NotifySelectionChanged();
            }
            catch (Exception ex) when (IsInputException(ex))
            {
                RequestStop("撤销坐标后的状态更新失败，拾点已停止：" + ex.Message);
                throw;
            }
            SolidWorksFileLogger.Info("已撤销路线最后一个已确认点；剩余点数=" + _selection.Points.Count + "。");
            return true;
        }

        /// <summary>请求重新编辑最后一个点；具体窗口由调用方提供。</summary>
        public bool TryEditLastPoint(out string error)
        {
            RequireAvailable();
            var points = _selection.Points;
            if (points.Count == 0)
            {
                error = "尚未选择坐标，无法编辑。";
                return false;
            }
            PipePoint3D? previous = points.Count > 1 ? points[points.Count - 2] : (PipePoint3D?)null;
            try { QueueConfirmation(points[points.Count - 1], previous, true); }
            catch (Exception ex) when (IsInputException(ex))
            {
                RequestStop("编辑坐标前的状态更新失败，拾点已停止：" + ex.Message);
                throw;
            }
            error = string.Empty;
            return true;
        }

        /// <summary>完成点列并清理事件；若清理抛错，调用方须先重试释放，不能开始建模。</summary>
        public bool TryComplete(out PipeRoute? route, out string error)
        {
            RequireAvailable();
            if (!_selection.TryComplete(out route, out error)) return false;
            DisposeCore();
            SolidWorksFileLogger.Info("路线点列已确认完成；点数=" + route!.Points.Count + "。");
            return true;
        }

        public void Cancel()
        {
            RequireAvailable();
            if (!_selection.TryCancel(out string error)) throw new InvalidOperationException(error);
            DisposeCore();
            SolidWorksFileLogger.Info("用户已取消本次路线拾点。");
        }

        public void Dispose()
        {
            RequireUiThread();
            DisposeCore();
        }

        /// <summary>只在事件清理完成后交出独立路线快照，避免新旧拾点会话重叠。</summary>
        public PipelineRouteSelectionSession? CopyPausedSelection()
        {
            RequireUiThread();
            return _disposed ? _pausedSelection?.CopySelecting() : null;
        }

        public bool HasPausedSelection => _pausedSelection != null;

        private int OnCommandOpening(int command, int userCommand)
        {
            if (_started && !_stopping && !_disposed)
            {
                _pausedSelection = _selection.CopySelecting();
                RequestStop("检测到 SolidWorks 新命令，路线选点已暂停；请结束该命令后点击“恢复选点”。");
            }
            return 0;
        }

        private int OnMouseMove(int x, int y, int flags)
        {
            if (!_started || _stopping || _disposed || _confirming) return 0;
            try
            {
                ValidateMousePosition(x, y);
                _latestX = x;
                _latestY = y;
                _pendingMove = true;
            }
            catch (Exception ex) when (IsInputException(ex))
            {
                try
                {
                    _pendingMove = false;
                    ClearCandidate();
                    _pendingPreviewRefresh = true;
                    PublishMessage("无法读取鼠标坐标：" + ex.Message);
                }
                catch (Exception callbackError) when (IsInputException(callbackError))
                {
                    RequestStop("路线拾点状态更新失败：" + callbackError.Message);
                }
            }
            return 0;
        }

        private int OnMouseDown(int x, int y, int flags)
        {
            if (!_started || _stopping || _disposed) return 0;
            if (_confirming) return 1;
            try
            {
                if (!IsCurrent())
                {
                    RequestStop("活动文档或视图已切换，路线拾点已停止。");
                    return 1;
                }
                ValidateMousePosition(x, y);
                PipePoint3D point = Project(x, y);
                var points = _selection.Points;
                PipePoint3D? previous = points.Count > 0 ? points[points.Count - 1] : (PipePoint3D?)null;
                QueueConfirmation(point, previous, false);
            }
            catch (Exception ex) when (IsInputException(ex))
            {
                try { PublishMessage("无法确认拾点坐标：" + ex.Message); }
                catch (Exception callbackError) when (IsInputException(callbackError))
                {
                    RequestStop("路线拾点状态更新失败：" + callbackError.Message);
                }
            }
            return 1;
        }

        private void OnMoveTick(object? sender, EventArgs args)
        {
            if (!_started || _stopping || _disposed || _confirming) return;
            try
            {
                if (!IsCurrent())
                {
                    RequestStop("活动文档或视图已切换，路线拾点已停止。");
                    return;
                }
                if (_pendingPreviewRefresh)
                {
                    _pendingPreviewRefresh = false;
                    RefreshPreview();
                }
                if (!IsCursorInsideView())
                {
                    _pendingMove = false;
                    if (_candidate.HasValue)
                    {
                        ClearCandidate();
                        RefreshPreview();
                    }
                    return;
                }
                if (!_pendingMove) return;
                _pendingMove = false;
                _candidate = Project(_latestX, _latestY);
                InvokeUiCallback(() => _cursorChanged(_candidate), "动态坐标显示");
                RefreshPreview();
                _lastMessage = string.Empty;
            }
            catch (Exception ex) when (IsInputException(ex))
            {
                try
                {
                    _pendingMove = false;
                    ClearCandidate();
                    RefreshPreview();
                    PublishMessage("无法计算拾点坐标：" + ex.Message);
                }
                catch (Exception recoveryError) when (IsInputException(recoveryError))
                {
                    RequestStop("更新路线拾点状态失败，拾点已停止：" + recoveryError.Message);
                }
            }
        }

        private PipePoint3D Project(int x, int y)
        {
            PipeRayIntersectionResult result = PipelineViewPointProjection.IntersectWorkPlane(
                _view, x, y, _selection.WorkPlane, out PipePoint3D point);
            if (result != PipeRayIntersectionResult.Hit)
                throw new InvalidOperationException("鼠标射线无法与 " + _selection.WorkPlane.Kind +
                    " 工作平面求交：" + result + "。请调整视图或切换平面。");
            return point;
        }

        private void QueueConfirmation(PipePoint3D point, PipePoint3D? previous, bool edit)
        {
            _pendingMove = false;
            ClearCandidate();
            RefreshPreview();
            _confirming = true;
            try
            {
                _dispatcher.BeginInvoke(new Action(() => ConfirmOnUiThread(point, previous, edit)),
                    DispatcherPriority.Normal);
            }
            catch (InvalidOperationException)
            {
                _confirming = false;
                throw;
            }
        }

        private void ConfirmOnUiThread(PipePoint3D point, PipePoint3D? previous, bool edit)
        {
            try
            {
                if (_stopping || _disposed) return;
                if (!IsCurrent())
                {
                    RequestStop("活动文档或视图已切换，路线拾点已停止。");
                    return;
                }
                PipePoint3D? confirmed = InvokeUiCallback(() => _confirmPoint(point, previous), "坐标确认窗口");
                if (_stopping || _disposed || !confirmed.HasValue) return;
                if (!IsCurrent())
                {
                    RequestStop("坐标确认期间活动文档或视图已切换，路线拾点已停止。");
                    return;
                }
                string error;
                bool accepted = edit
                    ? _selection.TryEditLastPoint(confirmed.Value, out error)
                    : _selection.TryAddPoint(confirmed.Value, out error);
                if (!accepted) PublishMessage(error);
                else
                {
                    SolidWorksFileLogger.Info((edit ? "已编辑路线最后一点" : "已确认路线坐标") +
                        "；坐标（毫米）=" + Describe(confirmed.Value) + "；当前点数=" + _selection.Points.Count + "。");
                    RefreshPreview();
                    NotifySelectionChanged();
                }
            }
            catch (Exception ex) when (IsInputException(ex))
            {
                SolidWorksFileLogger.Error("路线坐标确认失败：" + ex);
                RequestStop("坐标确认失败，路线拾点已停止：" + ex.Message);
            }
            finally { _confirming = false; }
        }

        private void RefreshPreview() => _preview.Update(_selection.Points, _candidate);

        private void NotifySelectionChanged() =>
            InvokeUiCallback(() => _selectionChanged(_selection.Points, _selection.WorkPlane), "路线状态显示");

        private static string Describe(PipePoint3D point) =>
            point.X.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "," +
            point.Y.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "," +
            point.Z.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

        private void ClearCandidate()
        {
            _pendingMove = false;
            if (!_candidate.HasValue) return;
            _candidate = null;
            InvokeUiCallback(() => _cursorChanged(null), "动态坐标显示");
        }

        private void PublishMessage(string message)
        {
            if (message == _lastMessage) return;
            _lastMessage = message;
            SolidWorksFileLogger.Warning("路线拾点：" + message);
            InvokeUiCallback(() => _message(message), "拾点提示");
        }

        private bool IsCursorInsideView()
        {
            if (!GetCursorPos(out NativePoint cursor) || !ScreenToClient(_viewWindow, ref cursor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取视图鼠标坐标。");
            if (!GetClientRect(_viewWindow, out NativeRect rect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取视图客户区。");
            return cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
        }

        private void ValidateMousePosition(int x, int y)
        {
            if (!GetCursorPos(out NativePoint cursor) || !ScreenToClient(_viewWindow, ref cursor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取视图鼠标坐标。");
            if (!GetClientRect(_viewWindow, out NativeRect rect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取视图客户区。");
            if (x < rect.Left || x >= rect.Right || y < rect.Top || y >= rect.Bottom ||
                Math.Abs((long)x - cursor.X) > 8 || Math.Abs((long)y - cursor.Y) > 8)
                throw new InvalidOperationException("鼠标事件位置与视图窗口坐标不一致，请重新移动鼠标。");
        }

        private bool IsCurrent()
        {
            var activeDocument = _application.ActiveDoc as ModelDoc2;
            if (activeDocument == null || _application.IsSame(activeDocument, _document) != (int)swObjectEquality.swObjectSame)
                return false;
            var activeView = _document.ActiveView as ModelView;
            return activeView != null && _application.IsSame(activeView, _view) == (int)swObjectEquality.swObjectSame &&
                new IntPtr(_view.GetViewHWndx64()) == _viewWindow;
        }

        private void OnPreviewStopped(string reason) => StopAndNotify(reason);

        private void RequestStop(string reason)
        {
            if (_stopping || _disposed) return;
            _stopping = true;
            _moveTimer.Stop();
            _dispatcher.BeginInvoke(new Action(() => StopAndNotify(reason)), DispatcherPriority.Background);
        }

        private void StopAndNotify(string reason)
        {
            if (_disposed) return;
            string message = reason;
            try { DisposeCore(); }
            catch (AggregateException ex) { message += " 清理失败：" + ex.Message; }
            SolidWorksFileLogger.Warning("路线拾点已停止：" + message);
            try { InvokeUiCallback(() => _unexpectedStop(message), "拾点停止通知"); }
            catch (InvalidOperationException ex)
            {
                SolidWorksFileLogger.Error("路线拾点停止通知失败：" + ex);
                System.Windows.MessageBox.Show(message + "\n通知界面更新失败：" + ex.Message,
                    "路线拾点已停止", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void DisposeCore()
        {
            if (_disposed) return;
            _stopping = true;
            _moveTimer.Stop();
            _moveTimer.Tick -= OnMoveTick;
            var failures = new List<Exception>();
            if (_commandSubscribed)
            {
                try
                {
                    _applicationEvents.CommandOpenPreNotify -= _commandOpening;
                    _commandSubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            if (_moveSubscribed)
            {
                try
                {
                    _mouseEvents.MouseMoveNotify -= _mouseMove;
                    _moveSubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            if (_downSubscribed)
            {
                try
                {
                    _mouseEvents.MouseLBtnDownNotify -= _mouseDown;
                    _downSubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            try { _preview.Dispose(); }
            catch (Exception ex) when (ex is AggregateException || IsInputException(ex)) { failures.Add(ex); }
            if (_selection.Status == PipelineRouteSelectionStatus.Selecting &&
                !_selection.TryCancel(out string error))
                failures.Add(new InvalidOperationException(error));
            Exception? notificationFailure = null;
            if (_candidate.HasValue)
            {
                _candidate = null;
                try
                {
                    InvokeUiCallback(() => _cursorChanged(null), "动态坐标显示");
                }
                catch (Exception ex) when (IsInputException(ex)) { notificationFailure = ex; }
            }
            GC.KeepAlive(_mouse);
            if (failures.Count == 0)
            {
                _disposed = true;
                SolidWorksFileLogger.Info("已清理 SolidWorks 路线鼠标拾点会话。");
            }
            if (notificationFailure != null) failures.Add(notificationFailure);
            if (failures.Count != 0)
                throw new AggregateException("路线拾点会话清理失败。", failures);
        }

        private void RequireAvailable()
        {
            RequireUiThread();
            if (!_started || _disposed || _stopping)
                throw new InvalidOperationException("路线拾点会话尚未启动或已结束。");
            if (_confirming) throw new InvalidOperationException("请先完成或取消当前坐标确认。");
            if (!IsCurrent())
            {
                RequestStop("活动文档或视图已切换，路线拾点已停止。");
                throw new InvalidOperationException("活动文档或视图已切换，不能继续拾点。");
            }
        }

        private void RequireUiThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _managedThreadId || !_dispatcher.CheckAccess())
                throw new InvalidOperationException("路线拾点必须在原 SolidWorks 界面线程操作。");
        }

        private static bool IsInputException(Exception ex) =>
            IsComLifetimeException(ex) || ex is InvalidOperationException || ex is NotSupportedException ||
            ex is ArithmeticException || ex is ArgumentException || ex is Win32Exception ||
            ex is DllNotFoundException || ex is EntryPointNotFoundException;

        private static bool IsComLifetimeException(Exception ex) =>
            ex is COMException || ex is InvalidComObjectException;

        private static T InvokeUiCallback<T>(Func<T> callback, string operation)
        {
            try { return callback(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is StackOverflowException) &&
                                       !(ex is AccessViolationException))
            {
                throw new InvalidOperationException(operation + "失败：" + ex.Message, ex);
            }
        }

        private static void InvokeUiCallback(Action callback, string operation)
        {
            InvokeUiCallback(() => { callback(); return true; }, operation);
        }
    }
}

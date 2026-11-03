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
    /// <summary>管理一个视图中的临时路线预览；所有操作必须在 SolidWorks 主界面线程执行。</summary>
    internal sealed class PipelineRoutePreviewSession : IDisposable
    {
        private readonly SldWorks _application;
        private readonly ModelDoc2 _document;
        private readonly ModelView _view;
        private readonly DSldWorksEvents_Event _applicationEvents;
        private readonly DModelViewEvents_Event _viewEvents;
        private readonly DAssemblyDocEvents_Event? _assemblyEvents;
        private readonly DPartDocEvents_Event? _partEvents;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _redrawTimer;
        private readonly Action<string> _unexpectedStop;
        private readonly int _managedThreadId;
        private readonly DSldWorksEvents_ActiveDocChangeNotifyEventHandler _documentChanged;
        private readonly DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler _modelDocumentChanged;
        private readonly DModelViewEvents_DestroyNotify2EventHandler _viewDestroyed;
        private readonly DModelViewEvents_GraphicsRenderPostNotifyEventHandler _render;
        private readonly DAssemblyDocEvents_ActiveViewChangeNotifyEventHandler? _assemblyViewChanged;
        private readonly DPartDocEvents_ActiveViewChangeNotifyEventHandler? _partViewChanged;
        private readonly List<PipePoint3D> _points = new List<PipePoint3D>();

        private bool _documentSubscribed;
        private bool _modelDocumentSubscribed;
        private bool _viewDestroySubscribed;
        private bool _renderSubscribed;
        private bool _documentViewSubscribed;
        private bool _startAttempted;
        private bool _started;
        private bool _stopping;
        private bool _disposed;
        private bool _viewIsDestroyed;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        public PipelineRoutePreviewSession(SldWorks application, ModelDoc2 document, Action<string> unexpectedStop)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _unexpectedStop = unexpectedStop ?? throw new ArgumentNullException(nameof(unexpectedStop));
            RequireSolidWorksUiThread(application);
            _managedThreadId = Thread.CurrentThread.ManagedThreadId;
            _dispatcher = Dispatcher.CurrentDispatcher;

            var active = application.ActiveDoc as ModelDoc2;
            if (active == null || !IsSame(application, active, document))
                throw new InvalidOperationException("路线预览只能在当前活动文档中启动。");
            int documentType = document.GetType();
            if (documentType != (int)swDocumentTypes_e.swDocASSEMBLY &&
                documentType != (int)swDocumentTypes_e.swDocPART)
                throw new NotSupportedException("路线预览仅支持零件或装配体文档。");
            _view = document.ActiveView as ModelView ??
                throw new InvalidOperationException("无法获取当前 SolidWorks 模型视图。");
            if (_view.HasPerspective())
                throw new NotSupportedException("透视视图的路线预览尚未完成验证，请关闭透视后重试。");
            _applicationEvents = application as DSldWorksEvents_Event ??
                throw new InvalidOperationException("无法订阅 SolidWorks 活动文档事件。");
            _viewEvents = _view as DModelViewEvents_Event ??
                throw new InvalidOperationException("无法订阅 SolidWorks 视图事件。");
            if (documentType == (int)swDocumentTypes_e.swDocASSEMBLY)
                _assemblyEvents = document as DAssemblyDocEvents_Event ??
                    throw new InvalidOperationException("无法订阅装配体活动视图事件。");
            else
                _partEvents = document as DPartDocEvents_Event ??
                    throw new InvalidOperationException("无法订阅零件活动视图事件。");

            _documentChanged = OnDocumentChanged;
            _modelDocumentChanged = OnModelDocumentChanged;
            _viewDestroyed = OnViewDestroyed;
            _render = OnRender;
            if (_assemblyEvents != null) _assemblyViewChanged = OnAssemblyViewChanged;
            if (_partEvents != null) _partViewChanged = OnPartViewChanged;
            _redrawTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _redrawTimer.Tick += OnRedrawTick;
        }

        /// <summary>订阅事件；失败时调用方仍持有会话，可在清理失败后重试释放。</summary>
        public void Start()
        {
            RequireUiThread();
            if (_startAttempted || _disposed || _stopping)
                throw new InvalidOperationException("路线预览会话已经启动或结束，不能再次启动。");
            if (!IsCurrent())
                throw new InvalidOperationException("活动文档或视图已切换，不能启动路线预览。");
            if (_view.HasPerspective())
                throw new NotSupportedException("透视视图的路线预览尚未完成验证，请关闭透视后重试。");
            _startAttempted = true;
            try
            {
                _applicationEvents.ActiveDocChangeNotify += _documentChanged;
                _documentSubscribed = true;
                _applicationEvents.ActiveModelDocChangeNotify += _modelDocumentChanged;
                _modelDocumentSubscribed = true;
                _viewEvents.DestroyNotify2 += _viewDestroyed;
                _viewDestroySubscribed = true;
                _viewEvents.GraphicsRenderPostNotify += _render;
                _renderSubscribed = true;
                if (_assemblyEvents != null)
                    _assemblyEvents.ActiveViewChangeNotify += _assemblyViewChanged;
                else
                    _partEvents!.ActiveViewChangeNotify += _partViewChanged;
                _documentViewSubscribed = true;
                if (!IsCurrent())
                    throw new InvalidOperationException("启动路线预览时活动文档或视图已切换。");
                _started = true;
            }
            catch (Exception ex) when (IsPreviewException(ex))
            {
                try { DisposeCore(false); }
                catch (AggregateException cleanupError)
                {
                    throw new AggregateException("启动路线预览失败，且清理未完成。", ex, cleanupError);
                }
                throw;
            }
            SolidWorksFileLogger.Info("已启动 SolidWorks 路线临时预览会话。");
        }

        /// <summary>替换已确认点及可选候选点，合并短时间内的重绘请求。</summary>
        public void Update(IReadOnlyList<PipePoint3D> confirmedPoints, PipePoint3D? candidate)
        {
            RequireUiThread();
            if (confirmedPoints == null) throw new ArgumentNullException(nameof(confirmedPoints));
            if (!_started || _disposed || _stopping)
                throw new InvalidOperationException("路线预览会话尚未启动或已结束。");
            if (!IsCurrent())
            {
                RequestStop("活动文档或视图已切换，路线预览已停止。");
                throw new InvalidOperationException("活动文档或视图已切换，不能继续更新路线预览。");
            }

            var copy = new List<PipePoint3D>(confirmedPoints.Count + 1);
            for (int i = 0; i < confirmedPoints.Count; i++) copy.Add(confirmedPoints[i]);
            if (candidate.HasValue && copy.Count > 0 && copy[copy.Count - 1] != candidate.Value)
                copy.Add(candidate.Value);
            _points.Clear();
            _points.AddRange(copy);
            if (!_redrawTimer.IsEnabled) _redrawTimer.Start();
        }

        public void Dispose()
        {
            RequireUiThread();
            DisposeCore(true);
        }

        private void OnRedrawTick(object? sender, EventArgs args)
        {
            _redrawTimer.Stop();
            if (_disposed || _stopping) return;
            try
            {
                if (!IsCurrent())
                {
                    RequestStop("活动文档或视图已切换，路线预览已停止。");
                    return;
                }
                _document.GraphicsRedraw2();
            }
            catch (Exception ex) when (IsPreviewException(ex))
            {
                RequestStop("路线预览重绘失败：" + ex.Message);
            }
        }

        private int OnRender()
        {
            if (!_started || _disposed || _stopping || _points.Count < 2) return 0;
            try
            {
                if (!IsCurrent())
                {
                    RequestStop("活动文档或视图已切换，路线预览已停止。");
                    return 0;
                }
                var lines = PipelineRouteOverlayProjection.Project(_view, _points);
                if (!PipelineScreenLineRenderer.TryDraw(lines, out string error))
                    RequestStop("路线预览绘制失败：" + error);
            }
            catch (Exception ex) when (IsPreviewException(ex))
            {
                RequestStop("路线预览绘制失败：" + ex.Message);
            }
            return 0;
        }

        private int OnDocumentChanged() { CheckCurrent(); return 0; }
        private int OnModelDocumentChanged() { CheckCurrent(); return 0; }
        private int OnAssemblyViewChanged() { CheckCurrent(); return 0; }
        private int OnPartViewChanged() { CheckCurrent(); return 0; }

        private void CheckCurrent()
        {
            if (!_started || _disposed || _stopping) return;
            try
            {
                if (!IsCurrent()) RequestStop("活动文档或视图已切换，路线预览已停止。");
            }
            catch (Exception ex) when (IsComLifetimeException(ex))
            {
                RequestStop("检查路线预览文档或视图失败：" + ex.Message);
            }
        }

        private int OnViewDestroyed(int destroyType)
        {
            _viewIsDestroyed = true;
            RequestStop("路线预览视图已关闭，预览已停止。");
            return 0;
        }

        private bool IsCurrent()
        {
            var activeDocument = _application.ActiveDoc as ModelDoc2;
            if (activeDocument == null || !IsSame(_application, activeDocument, _document)) return false;
            var activeView = _document.ActiveView as ModelView;
            return activeView != null && IsSame(_application, activeView, _view);
        }

        private void RequestStop(string reason)
        {
            if (_disposed || _stopping) return;
            _stopping = true;
            _redrawTimer.Stop();
            SolidWorksFileLogger.Warning(reason);
            // 避免在 SolidWorks 的渲染或销毁回调内部直接取消 COM 事件订阅。
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                string message = reason;
                try { DisposeCore(true); }
                catch (AggregateException ex)
                {
                    SolidWorksFileLogger.Error("路线预览清理失败：" + ex.Message);
                    message += " 清理失败：" + ex.Message;
                }
                _unexpectedStop(message);
            }), DispatcherPriority.Background);
        }

        private void DisposeCore(bool redraw)
        {
            if (_disposed) return;
            _stopping = true;
            _redrawTimer.Stop();
            _redrawTimer.Tick -= OnRedrawTick;
            var failures = new List<Exception>();
            if (_documentViewSubscribed)
            {
                try
                {
                    if (_assemblyEvents != null) _assemblyEvents.ActiveViewChangeNotify -= _assemblyViewChanged;
                    else _partEvents!.ActiveViewChangeNotify -= _partViewChanged;
                    _documentViewSubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            if (_renderSubscribed)
            {
                try
                {
                    _viewEvents.GraphicsRenderPostNotify -= _render;
                    _renderSubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            if (_viewDestroySubscribed)
            {
                try
                {
                    _viewEvents.DestroyNotify2 -= _viewDestroyed;
                    _viewDestroySubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            if (_documentSubscribed)
            {
                try
                {
                    _applicationEvents.ActiveDocChangeNotify -= _documentChanged;
                    _documentSubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            if (_modelDocumentSubscribed)
            {
                try
                {
                    _applicationEvents.ActiveModelDocChangeNotify -= _modelDocumentChanged;
                    _modelDocumentSubscribed = false;
                }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            _points.Clear();
            if (redraw && _started && !_viewIsDestroyed)
            {
                try { _document.GraphicsRedraw2(); }
                catch (Exception ex) when (IsComLifetimeException(ex)) { failures.Add(ex); }
            }
            if (failures.Count != 0)
                throw new AggregateException("路线预览事件或图形清理失败。", failures);
            _disposed = true;
            SolidWorksFileLogger.Info("已清理 SolidWorks 路线临时预览会话。");
        }

        private void RequireUiThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _managedThreadId || !_dispatcher.CheckAccess())
                throw new InvalidOperationException("路线预览必须在原 SolidWorks 界面线程操作。");
        }

        private static void RequireSolidWorksUiThread(SldWorks application)
        {
            var frame = application.Frame() as Frame;
            if (frame == null) throw new InvalidOperationException("无法获取 SolidWorks 主窗口。");
            IntPtr window = new IntPtr(frame.GetHWndx64());
            uint thread = GetWindowThreadProcessId(window, out uint process);
            if (window == IntPtr.Zero || thread == 0 || thread != GetCurrentThreadId() ||
                process != GetCurrentProcessId())
                throw new InvalidOperationException("路线预览必须在 SolidWorks 主窗口所属进程的界面线程启动。");
        }

        private static bool IsSame(SldWorks application, object left, object right) =>
            application.IsSame(left, right) == (int)swObjectEquality.swObjectSame;

        private static bool IsPreviewException(Exception ex) =>
            IsComLifetimeException(ex) || ex is InvalidOperationException || ex is NotSupportedException ||
            ex is ArithmeticException || ex is ArgumentException || ex is Win32Exception ||
            ex is DllNotFoundException || ex is EntryPointNotFoundException;

        private static bool IsComLifetimeException(Exception ex) =>
            ex is COMException || ex is InvalidComObjectException;
    }
}

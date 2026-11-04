using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Helpers
{
    internal enum PipelineRouteShortcut { Undo, Edit, Complete, Cancel }

    internal enum PipelineRouteKeyDecision { Pass, Suppress, Dispatch }

    /// <summary>按键按下、重复和释放成对处理；不依赖窗口或 SolidWorks 对象。</summary>
    internal sealed class PipelineRouteKeyboardFilter
    {
        private readonly HashSet<int> _pressed = new HashSet<int>();

        public PipelineRouteKeyDecision Process(int key, long flags, bool eligible, bool modifiers,
            out PipelineRouteShortcut shortcut)
        {
            if (!TryMapKey(key, out shortcut)) return PipelineRouteKeyDecision.Pass;
            if (!eligible)
            {
                _pressed.Clear();
                return PipelineRouteKeyDecision.Pass;
            }
            bool released = (flags & (1L << 31)) != 0;
            bool repeated = (flags & (1L << 30)) != 0;
            bool wasHandled = _pressed.Contains(key);
            if (released)
            {
                _pressed.Remove(key);
                return wasHandled && eligible ? PipelineRouteKeyDecision.Suppress : PipelineRouteKeyDecision.Pass;
            }
            if (repeated)
                return wasHandled && eligible ? PipelineRouteKeyDecision.Suppress : PipelineRouteKeyDecision.Pass;
            _pressed.Remove(key);
            if ((flags & (1L << 29)) != 0 || modifiers) return PipelineRouteKeyDecision.Pass;
            _pressed.Add(key);
            return PipelineRouteKeyDecision.Dispatch;
        }

        public void Clear() { _pressed.Clear(); }

        internal static bool TryMapKey(int virtualKey, out PipelineRouteShortcut shortcut)
        {
            switch (virtualKey)
            {
                case 0x5A: shortcut = PipelineRouteShortcut.Undo; return true;
                case 0x58: shortcut = PipelineRouteShortcut.Edit; return true;
                case 0x20: shortcut = PipelineRouteShortcut.Complete; return true;
                case 0x1B: shortcut = PipelineRouteShortcut.Cancel; return true;
                default: shortcut = default(PipelineRouteShortcut); return false;
            }
        }
    }

    /// <summary>仅在目标图形视图取得焦点时接管本次路线选点快捷键。</summary>
    internal sealed class PipelineRouteKeyboardHook : IDisposable
    {
        private const int WhKeyboard = 2;
        private readonly IntPtr _viewWindow;
        private readonly IntPtr _mainWindow;
        private readonly Func<bool> _enabled;
        private readonly Action<PipelineRouteShortcut> _execute;
        private readonly Action<Exception> _fault;
        private readonly Dispatcher _dispatcher;
        private readonly int _managedThreadId;
        private readonly HookProcedure _procedure;
        private readonly PipelineRouteKeyboardFilter _filter = new PipelineRouteKeyboardFilter();
        private IntPtr _hook;
        private bool _accepting;
        private bool _disposed;

        private delegate IntPtr HookProcedure(int code, IntPtr key, IntPtr flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int type, HookProcedure callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr key, IntPtr flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        public PipelineRouteKeyboardHook(IntPtr viewWindow, IntPtr mainWindow, Func<bool> enabled,
            Action<PipelineRouteShortcut> execute, Action<Exception> fault)
        {
            if (viewWindow == IntPtr.Zero) throw new ArgumentException("路线视图窗口句柄无效。", nameof(viewWindow));
            if (mainWindow == IntPtr.Zero) throw new ArgumentException("SolidWorks 主窗口句柄无效。", nameof(mainWindow));
            _viewWindow = viewWindow;
            _mainWindow = mainWindow;
            _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _fault = fault ?? throw new ArgumentNullException(nameof(fault));
            _dispatcher = Dispatcher.CurrentDispatcher;
            _managedThreadId = Thread.CurrentThread.ManagedThreadId;
            _procedure = OnKeyboard;
        }

        public void Start()
        {
            RequireUiThread();
            if (_disposed || _hook != IntPtr.Zero)
                throw new InvalidOperationException("路线快捷键监听已经启动或结束。");
            uint currentThread = GetCurrentThreadId();
            uint currentProcess = GetCurrentProcessId();
            if (GetWindowThreadProcessId(_viewWindow, out uint viewProcess) != currentThread ||
                viewProcess != currentProcess ||
                GetWindowThreadProcessId(_mainWindow, out uint mainProcess) != currentThread ||
                mainProcess != currentProcess)
                throw new InvalidOperationException("路线快捷键只能在 SolidWorks 视图所属界面线程启用。");
            _hook = SetWindowsHookEx(WhKeyboard, _procedure, IntPtr.Zero, currentThread);
            if (_hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法启用路线快捷键。");
            _accepting = true;
        }

        public void Dispose()
        {
            RequireUiThread();
            if (_disposed) return;
            _accepting = false;
            _filter.Clear();
            if (_hook != IntPtr.Zero)
            {
                if (!UnhookWindowsHookEx(_hook))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法释放路线快捷键监听。");
                _hook = IntPtr.Zero;
            }
            _disposed = true;
        }

        private IntPtr OnKeyboard(int code, IntPtr key, IntPtr flags)
        {
            if (code != 0 || !_accepting) return CallNextHookEx(_hook, code, key, flags);
            try
            {
                int virtualKey = unchecked((int)key.ToInt64());
                if (!PipelineRouteKeyboardFilter.TryMapKey(virtualKey, out _))
                    return CallNextHookEx(_hook, code, key, flags);
                PipelineRouteKeyDecision decision = _filter.Process(virtualKey, flags.ToInt64(),
                    IsTargetFocus(), HasModifiers(), out PipelineRouteShortcut shortcut);
                if (decision == PipelineRouteKeyDecision.Pass) return CallNextHookEx(_hook, code, key, flags);
                if (decision == PipelineRouteKeyDecision.Dispatch)
                {
                    // 钩子中只投递操作；任何窗口和 SolidWorks 调用都留给界面消息循环。
                    DispatcherOperation pending = _dispatcher.BeginInvoke(
                        new Action(() => ExecuteIfStillEligible(shortcut)), DispatcherPriority.Input);
                    if (pending.Status == DispatcherOperationStatus.Aborted)
                        throw new InvalidOperationException("路线快捷键操作无法投递到界面线程。");
                }
                return new IntPtr(1);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                _accepting = false;
                _filter.Clear();
                try { _dispatcher.BeginInvoke(new Action(() => _fault(ex)), DispatcherPriority.Normal); }
                catch (InvalidOperationException dispatchError)
                {
                    SolidWorksFileLogger.Error("路线快捷键异常通知无法投递：" + dispatchError);
                }
                return CallNextHookEx(_hook, code, key, flags);
            }
        }

        private void ExecuteIfStillEligible(PipelineRouteShortcut shortcut)
        {
            try
            {
                if (!_accepting || !IsTargetFocus()) return;
                _execute(shortcut);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                _accepting = false;
                _filter.Clear();
                _fault(ex);
            }
        }

        private bool IsTargetFocus() =>
            GetForegroundWindow() == _mainWindow && GetFocus() == _viewWindow && _enabled();

        private static bool HasModifiers() =>
            IsDown(0x10) || IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);

        // 使用消息队列的修饰键状态，避免界面忙时把已释放的 Ctrl+Z 误判为普通 Z。
        private static bool IsDown(int key) => (GetKeyState(key) & 0x8000) != 0;

        private void RequireUiThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _managedThreadId || !_dispatcher.CheckAccess())
                throw new InvalidOperationException("路线快捷键必须在原 SolidWorks 界面线程操作。");
        }

        private static bool IsNonFatal(Exception ex) =>
            !(ex is OutOfMemoryException) && !(ex is StackOverflowException) && !(ex is AccessViolationException);
    }
}

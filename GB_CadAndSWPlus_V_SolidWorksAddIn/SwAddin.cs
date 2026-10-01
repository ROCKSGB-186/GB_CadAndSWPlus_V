using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using GB_CadAndSWPlus_V.Shared;
using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn
{
    /// <summary>
    /// GB 工程平台 SolidWorks 插件的主类，实现了 ISwAddin 接口，用于与 SolidWorks 进行交互。
    /// </summary>
    [ComVisible(true)]
    [Guid(AddInGuid)]
    [ProgId("GB_CadAndSWPlus_V.SolidWorksAddIn")]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class SwAddin : ISwAddin
    {
        /// <summary>
        /// GB 工程平台 SolidWorks 插件的唯一标识符，用于在注册表中注册插件信息。
        /// </summary>
        public const string AddInGuid = "2B3F8C3D-9D7A-4E89-9E3B-0D5B3C5C1C42";
        /// <summary>
        /// GB 工程平台 SolidWorks 插件的标题，用于在 SolidWorks 插件管理器中显示。
        /// </summary>
        private const string AddInTitle = "GB 工程平台";
        /// <summary>
        /// GB 工程平台 SolidWorks 插件的描述，用于在 SolidWorks 插件管理器中显示。
        /// </summary>
        private const string AddInDescription = "GB CAD/P&ID 与 SolidWorks 工程数据连接插件";
        /// <summary>
        /// 存储 SolidWorks 应用程序对象的引用，用于与 SolidWorks 进行交互。
        /// </summary>
        private SldWorks? _solidWorks;
        /// <summary>
        /// 存储 SolidWorks 插件的 Cookie，用于标识插件在 SolidWorks 中的唯一实例。
        /// </summary>
        private int _cookie;
        private ICommandManager? _commandManager;
        private ICommandGroup? _commandGroup;
        private readonly List<Window> _openWindows = new List<Window>();
        private SwUserSession _userSession = new SwUserSession();

        private const int CommandGroupId = 2022;

        // SolidWorks 会在加载插件后调用 ConnectToSW；这里是插件获得
        // SolidWorks COM 应用对象、Cookie 以及创建菜单/工具栏命令的入口。
        /// <summary>
        /// 连接到 SolidWorks 应用程序，并获取插件的 Cookie，用于标识插件在 SolidWorks 中的唯一实例。
        /// </summary>
        /// <param name="ThisSW">SolidWorks 应用程序对象。</param>
        /// <param name="Cookie">插件的唯一标识符。</param>
        /// <returns>如果成功连接到 SolidWorks，则返回 true；否则返回 false。</returns>
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            SolidWorksLog.Info("开始连接 SolidWorks 插件。");
            if (ThisSW == null)
            {
                SolidWorksLog.Warning("SolidWorks 传入对象为空，插件连接失败。");
                return false;
            }

            try
            {
                _solidWorks = ThisSW as SldWorks;
                if (_solidWorks == null)
                {
                    return false;
                }

                _cookie = Cookie;
                _userSession = SwUserSession.Load();
                // 将当前插件对象回传给 SolidWorks。后续 CommandManager 的回调方法
                // 会根据这里登记的对象，通过方法名反射调用 ShowPipelineWindow 等方法。
                _solidWorks.SetAddinCallbackInfo(0, this, _cookie);
                RegisterCommands();
                SolidWorksLog.Info($"SolidWorks 插件连接成功，命令组编号={CommandGroupId}。");
                return true;
            }
            catch (Exception ex)
            {
                SolidWorksLog.Error($"SolidWorks 插件连接失败：{ex.Message}");
                _solidWorks = null;
                _cookie = 0;
                return false;
            }
        }

        /// <summary>
        /// 断开与 SolidWorks 应用程序的连接，并清除插件的 Cookie。
        /// </summary>
        /// <returns>如果成功断开连接，则返回 true；否则返回 false。</returns>
        public bool DisconnectFromSW()
        {
            SolidWorksLog.Info($"开始断开 SolidWorks 插件，当前窗口数量={_openWindows.Count}。");
            // SolidWorks 退出或卸载插件时先关闭由插件创建的 WPF 窗口，
            // 避免窗口继续持有宿主句柄或 SolidWorks COM 对象。
            foreach (Window window in _openWindows.ToArray())
            {
                window.Close();
            }

            _openWindows.Clear();
            _commandGroup = null;
            _commandManager = null;
            _solidWorks = null;
            _cookie = 0;
            _userSession = new SwUserSession();
            SolidWorksLog.Info("SolidWorks 插件已断开。");
            return true;
        }

        private void RegisterCommands()
        {
            if (_solidWorks == null)
            {
                return;
            }

            // Cookie 用于取得当前插件专属的命令管理器，避免与其他插件的命令冲突。
            _commandManager = _solidWorks.GetCommandManager(_cookie);
            int errors = 0;
            // 一个 CommandGroup 可以同时生成 SolidWorks 菜单项和工具栏按钮。
            // 命令项中的回调名称必须与本类的公开方法名称一致。
            _commandGroup = _commandManager.CreateCommandGroup2(
                CommandGroupId,
                AddInTitle,
                AddInDescription,
                AddInDescription,
                -1,
                true,
                ref errors);

            _commandGroup.AddCommandItem2(
                "管道",
                -1,
                "打开管道页面",
                "打开管道页面",
                0,
                nameof(ShowPipelineWindow),
                nameof(IsCommandEnabled),
                1,
                0);
            _commandGroup.AddCommandItem2(
                "法兰",
                -1,
                "打开法兰页面",
                "打开法兰页面",
                0,
                nameof(ShowFlangeWindow),
                nameof(IsCommandEnabled),
                2,
                0);
            _commandGroup.AddCommandItem2(
                "页面面板",
                -1,
                "打开页面面板",
                "打开页面面板",
                0,
                nameof(ShowRightPanel),
                nameof(IsCommandEnabled),
                3,
                0);

            // 激活命令组后，SolidWorks 才会把菜单和工具栏真正显示出来。
            _commandGroup.HasToolbar = true;
            _commandGroup.HasMenu = true;
            _commandGroup.Activate();
        }

        public int IsCommandEnabled(int commandId)
        {
            // SolidWorks 约定返回 1 表示启用，返回 0 表示禁用。
            return _solidWorks == null ? 0 : 1;
        }

        public void ShowPipelineWindow()
        {
            SolidWorksLog.Info("收到打开管道页面命令。");
            if (!EnsureSignedIn())
            {
                return;
            }

            ShowOwnedWindow(new PipelineWindow(new IntPtr(GetSolidWorksWindowHandle())));
        }

        public void ShowFlangeWindow()
        {
            SolidWorksLog.Info("收到打开法兰页面命令。");
            if (!EnsureSignedIn())
            {
                return;
            }

            ShowOwnedWindow(new FlangeWindow(new IntPtr(GetSolidWorksWindowHandle())));
        }

        public void ShowRightPanel()
        {
            SolidWorksLog.Info("收到打开页面面板命令。");
            if (!EnsureSignedIn())
            {
                return;
            }

            var window = new Window
            {
                Title = AddInTitle,
                Width = 360,
                Height = 720,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                // 页面面板使用统一的 Page + Frame 导航结构；
                // 管道、法兰页面共享同一个 SolidWorks 应用对象。
                Content = new AddInMainPage(_solidWorks, _userSession)
            };

            ShowOwnedWindow(window);
        }

        private bool EnsureSignedIn()
        {
            if (_userSession.IsSignedIn)
            {
                SolidWorksLog.Info("检测到有效 SolidWorks 登录会话，继续执行操作。");
                return true;
            }

            if (_solidWorks == null)
            {
                SolidWorksLog.Warning("SolidWorks 尚未连接，无法执行登录。");
                return false;
            }

            var loginService = new UnifiedSwLoginService(new AuthService(), _userSession);
            var sessionStore = new SharedLoginSessionStore();
            try
            {
                if (!TrayLauncher.IsRunning())
                {
                    SolidWorksLog.Info("托盘程序未运行，跳过共享会话自动登录。");
                    goto ShowLoginWindow;
                }
                SolidWorksLog.Info("开始使用共享会话自动登录 SolidWorks。");
                SharedLoginSession sharedSession = sessionStore.Load();
                if (!string.IsNullOrWhiteSpace(sharedSession.ServerHost) &&
                    !string.IsNullOrWhiteSpace(sharedSession.Username) &&
                    !string.IsNullOrWhiteSpace(sharedSession.EncryptedPassword))
                {
                    UnifiedLoginResult autoLogin = loginService
                        .LoginWithSharedSessionAsync(sharedSession)
                        .GetAwaiter()
                        .GetResult();
                    if (autoLogin?.Success == true)
                    {
                        SolidWorksLog.Info("共享会话自动登录 SolidWorks 成功。");
                        return true;
                    }

                    SolidWorksLog.Warning("共享会话自动登录 SolidWorks 失败，清理失效会话。");
                    sessionStore.Clear();
                }
            }
            catch (Exception ex)
            {
                SolidWorksLog.Error("共享 SolidWorks 会话自动登录异常：" + ex.Message);
            }

        ShowLoginWindow:
            SolidWorksLog.Info("打开 SolidWorks 统一登录窗口。");
            var loginWindow = new UnifiedLoginWindow(
                UnifiedLoginPlatform.SolidWorks,
                loginService,
                sessionStore);
            try
            {
                loginWindow.Owner = null;
                bool? result = loginWindow.ShowDialog();
                if (result == true && loginWindow.LoginResult != null)
                {
                    SolidWorksLog.Info("SolidWorks 统一登录成功。");
                    return true;
                }
                SolidWorksLog.Warning("SolidWorks 统一登录窗口未完成登录。");
            }
            finally
            {
                loginWindow.Close();
            }

            return false;
        }

        private int GetSolidWorksWindowHandle()
        {
            if (_solidWorks == null)
            {
                throw new InvalidOperationException("SolidWorks 尚未连接。");
            }

            // WPF 窗口不是 SolidWorks 原生窗口，必须使用宿主 HWND 设置 Owner，
            // 这样窗口才能随 SolidWorks 最小化/恢复，并保持正确的前后关系。
            return _solidWorks.IFrameObject().GetHWnd();
        }

        private void ShowOwnedWindow(Window window)
        {
            if (_solidWorks == null)
            {
                window.Close();
                return;
            }

            // 保存窗口引用，断开插件时统一关闭；Closed 事件负责移除已关闭窗口。
            _openWindows.Add(window);
            window.Closed += (_, _) => _openWindows.Remove(window);
            window.Show();
        }

        /// <summary>
        /// 在注册表中注册 SolidWorks 插件信息。
        /// </summary>
        /// <param name="type">要注册的插件类型。</param>
        [ComRegisterFunction]
        public static void RegisterFunction(Type type)
        {
            // SolidWorks 根据 HKLM 下的 AddIns\{GUID} 查找插件，并读取标题和描述。
            // 写入 HKLM 通常需要管理员权限。
            string addInKeyPath = $"SOFTWARE\\SolidWorks\\AddIns\\{{{AddInGuid}}}";
            using (RegistryKey? addInKey = Registry.LocalMachine.CreateSubKey(addInKeyPath))
            {
                if (addInKey == null)
                {
                    throw new InvalidOperationException("无法创建 SolidWorks Add-in 注册表项，请使用管理员权限注册插件。");
                }

                addInKey.SetValue(null, 1, RegistryValueKind.DWord);
                addInKey.SetValue("Title", AddInTitle, RegistryValueKind.String);
                addInKey.SetValue("Description", AddInDescription, RegistryValueKind.String);
            }

            using (RegistryKey? startupKey = Registry.CurrentUser.CreateSubKey(
                $"Software\\SolidWorks\\AddInsStartup\\{{{AddInGuid}}}"))
            {
                // HKCU 项控制插件是否随 SolidWorks 启动自动加载；值 1 表示启用。
                startupKey?.SetValue(null, 1, RegistryValueKind.DWord);
            }
        }
        /// <summary>
        /// 在注册表中注销 SolidWorks 插件信息。
        /// </summary>
        /// <param name="type">要注销的插件类型。</param>
        [ComUnregisterFunction]
        public static void UnregisterFunction(Type type)
        {
            // 卸载插件时删除两个注册表位置，避免 SolidWorks 继续显示失效插件。
            Registry.LocalMachine.DeleteSubKeyTree(
                $"SOFTWARE\\SolidWorks\\AddIns\\{{{AddInGuid}}}", false);
            Registry.CurrentUser.DeleteSubKeyTree(
                $"Software\\SolidWorks\\AddInsStartup\\{{{AddInGuid}}}", false);
        }
    }
}

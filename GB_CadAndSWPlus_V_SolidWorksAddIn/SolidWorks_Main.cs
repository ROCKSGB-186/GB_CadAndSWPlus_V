using System.Runtime.InteropServices; // 引入 COM 互操作特性，用于注册 SolidWorks 插件入口。
using System; // 引入日期和事件类型。
using Microsoft.Win32; // 引入注册表 API，用于维护 SolidWorks Add-in 入口。
using System.Windows; // 引入 WPF 窗口和消息框类型。
using GB_CadAndSWPlus_V.Shared; // 引入共享统一登录窗口。
using GB_CadAndSWPlus_V.Shared.Models; // 引入统一登录平台枚举。
using GB_CadAndSWPlus_V.Shared.Services; // 引入共享登录会话存储。
using Xarial.XCad.SolidWorks; // 引入 XCAD 的 SolidWorks 插件基类。
using Xarial.XCad.UI; // 引入 XCAD 自定义面板接口。
using Xarial.XCad.UI.Commands; // 引入 XCAD 的命令组管理接口。
using GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages; // 引入统一的顶部命令组定义。
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services; // 引入 SolidWorks 专用登录服务。
using Xarial.XCad.Extensions;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn
{
    /// <summary>SolidWorks 插件的唯一 XCAD 主入口。</summary>
    [ComVisible(true)] // 将主入口暴露为 COM 类，供 SolidWorks 加载插件。
    [Guid(AddInGuid)] // 固定插件身份，避免每次生成 DLL 时产生新的 COM 类标识。
    [ProgId("GB_CadAndSWPlus_V.SolidWorksAddIn")] // 固定插件 ProgID，保持现有注册信息兼容。
    [ClassInterface(ClassInterfaceType.AutoDispatch)] // 使用自动分发接口，兼容 SolidWorks 的 COM 加载方式。
    public sealed class SolidWorks_Main : Xarial.XCad.SolidWorks.SwAddInEx
    {
        /// <summary>保持与原 SolidWorks 插件注册项一致的固定 GUID。</summary>
        public const string AddInGuid = "873CA660-27CA-3101-9314-9804FA2A307B";

        private const string AddInTitle = "GB_二\\三维设计数据互联平台"; // SolidWorks 插件管理器中显示的名称。
        private const string AddInDescription = " CAD/P&ID 、 SolidWorks等平台 辅助二\\三维设计数据互联插件 "; // 插件说明。

        private readonly SharedLoginSessionStore _sessionStore = new SharedLoginSessionStore(); // 创建与 CAD 和托盘共享的会话存储。
        private readonly UnifiedSolidWorksLoginService _loginService = new UnifiedSolidWorksLoginService(); // 创建 SolidWorks 登录服务。
        private IXCustomPanel<RightResourceLibrary>? _rightResourceLibrary; // 持有右侧资源库面板引用，确保其生命周期与插件一致。

        /// <summary>在 SolidWorks 连接插件时创建唯一的 XCAD 顶部命令组。</summary>
        public override void OnConnect() // XCAD 在插件连接完成后调用此方法。
        {
            SolidWorksFileLogger.Start("SolidWorks 插件连接初始化");
            var commandGroup = CommandManager.AddCommandGroup<TopMenuButton>(); // 创建唯一的统一顶部命令组。
            commandGroup.CommandClick += OnCommandClick; // 绑定 XCAD 命令点击事件。
            CreateRightResourceLibrary(); // 创建右侧资源库面板。
            SolidWorksFileLogger.Complete("SolidWorks 插件连接初始化");
        }

        /// <summary>在 SolidWorks 断开插件时执行清理入口。</summary>
        public override void OnDisconnect() // XCAD 在插件断开前调用此方法。
        {
            SolidWorksFileLogger.Start("SolidWorks 插件断开清理");
            if (_rightResourceLibrary?.IsControlCreated == true)
                _rightResourceLibrary.Control.CancelDrawingInsertPointSelection("SolidWorks 插件断开");
            // 当前命令组由 XCAD 管理，断开时不重复创建或手动删除命令组。
            SolidWorksFileLogger.Complete("SolidWorks 插件断开清理");
        }
        /// <summary>
        /// 处理顶部命令组按钮点击事件，根据用户点击的枚举命令执行对应功能。
        /// </summary>
        /// <param name="command">用户点击的顶部命令按钮枚举值。</param>
        private void OnCommandClick(TopMenuButton command) // 根据用户点击的枚举命令执行对应功能。
        {
            SolidWorksFileLogger.Start("顶部命令点击：" + command);
            switch (command) // 分发登录和退出两个顶部按钮。
            {
                case TopMenuButton.LogoIN: // 处理登录按钮点击。
                    ShowLoginWindow(); // 打开共享统一登录窗口。
                    break; // 结束登录命令处理。
                case TopMenuButton.Logout: // 处理退出按钮点击。
                    Logout(); // 清除共享认证信息。
                    break; // 结束退出命令处理。
                default:
                    SolidWorksFileLogger.Skipped("顶部命令点击", "未识别的命令");
                    break;
            }
            SolidWorksFileLogger.Complete("顶部命令点击：" + command);
        }
        /// <summary>
        /// 显示共享统一登录窗口。
        /// </summary>
        private void ShowLoginWindow() // 显示共享统一登录窗口。
        {
            SolidWorksFileLogger.Start("打开统一登录窗口");
            var loginWindow = new UnifiedLoginWindow(UnifiedLoginPlatform.SolidWorks, _loginService, _sessionStore); // 创建 SolidWorks 平台登录窗口。
            loginWindow.Owner = null; // 不设置 SolidWorks COM 窗口为 WPF Owner，避免跨句柄导致窗口无法激活。
            bool? loginResult = loginWindow.ShowDialog(); // 以模态方式显示登录窗口并等待用户操作。
            if (loginResult == true && loginWindow.LoginResult?.Success == true)
                SetResourceLibraryAuthenticationState(true); // 登录成功后才显示资源库业务内容。
            SolidWorksFileLogger.Complete(loginResult == true ? "统一登录窗口处理" : "统一登录窗口取消");
        }

        private void Logout() // 执行与托盘“退出登录”一致的认证清理操作。
        {
            SolidWorksFileLogger.Start("退出 SolidWorks 平台账号");
            _sessionStore.ClearAuthentication(); // 清除共享访问令牌和加密密码，但保留服务器连接配置。
            SetResourceLibraryAuthenticationState(false); // 退出登录后立即隐藏资源库业务内容。
            MessageBox.Show("已退出登录。", "GB_二\\三维设计数据互联平台", MessageBoxButton.OK, MessageBoxImage.Information); // 向用户提示退出登录结果。
            SolidWorksFileLogger.Complete("退出 SolidWorks 平台账号");
        }

        private void CreateRightResourceLibrary() // 创建右侧资源库面板（暂未使用）。
        {
            SolidWorksFileLogger.Start("创建右侧资源库面板");
            _rightResourceLibrary = this.CreateTaskPane<RightResourceLibrary>(); // 让 XCAD 创建并托管 WPF UserControl。
            _rightResourceLibrary.ControlCreated += ConfigureResourceLibraryControl; // 兼容 XCAD 延迟创建控件的情况。
            if (_rightResourceLibrary.IsControlCreated) // 只有控件已经创建后才能安全访问 Control 属性。
                ConfigureResourceLibraryControl(_rightResourceLibrary.Control); // 立即创建时直接完成相同初始化。
            SolidWorksFileLogger.Complete("创建右侧资源库面板");
        }

        /// <summary>初始化资源库面板，避免延迟创建和立即创建走两套逻辑。</summary>
        private void ConfigureResourceLibraryControl(RightResourceLibrary control)
        {
            SolidWorksFileLogger.Start("初始化右侧资源库面板");
            control.SW = Application.Sw; // 注入 SolidWorks 应用程序对象。
            control.LoginRequested -= ResourceLibrary_LoginRequested; // 防止 XCAD 重复触发初始化时重复绑定事件。
            control.LoginRequested += ResourceLibrary_LoginRequested; // 将登录链接连接到统一登录入口。
            control.SetAuthenticationState(IsAuthenticated()); // 按共享会话初始化资源库状态。
            SolidWorksFileLogger.Complete("初始化右侧资源库面板");
        }

        /// <summary>资源库占位页面请求登录时打开统一登录窗口。</summary>
        private void ResourceLibrary_LoginRequested(object sender, System.EventArgs e)
        {
            SolidWorksFileLogger.Info("资源库登录链接被点击。");
            ShowLoginWindow(); // 与顶部统一登录命令使用同一登录入口，避免复制登录逻辑。
        }

        /// <summary>根据共享会话判断当前是否存在未过期的认证令牌。</summary>
        private bool IsAuthenticated()
        {
            SolidWorksFileLogger.Start("检查 SolidWorks 登录状态");
            SharedLoginSession session = _sessionStore.Load();
            bool authenticated = !string.IsNullOrWhiteSpace(session.AccessToken)
                && (!session.AccessTokenExpiresAtUtc.HasValue || session.AccessTokenExpiresAtUtc.Value > DateTime.UtcNow);
            SolidWorksFileLogger.Complete("检查 SolidWorks 登录状态；结果=" + (authenticated ? "已登录" : "未登录"));
            return authenticated;
        }

        /// <summary>刷新资源库面板的登录显示状态。</summary>
        private void SetResourceLibraryAuthenticationState(bool authenticated)
        {
            SolidWorksFileLogger.Info("更新资源库认证显示状态；状态=" + (authenticated ? "已登录" : "未登录") + "。");
            if (_rightResourceLibrary != null && _rightResourceLibrary.IsControlCreated)
                _rightResourceLibrary.Control.SetAuthenticationState(authenticated);
        }

        /// <summary>将当前 DLL 注册为 SolidWorks Add-in，并更新当前 DLL 的 COM CodeBase。</summary>
        [ComRegisterFunction]
        public static new void RegisterFunction(Type type)
        {
            string addInKeyPath = $"SOFTWARE\\SolidWorks\\AddIns\\{{{AddInGuid}}}"; // SolidWorks 启动时查找的插件入口。
            using (RegistryKey? addInKey = Registry.LocalMachine.CreateSubKey(addInKeyPath))
            {
                if (addInKey == null)
                    throw new InvalidOperationException("无法创建 SolidWorks 插件注册项，请使用管理员权限运行 Visual Studio 后重新生成。 ");

                addInKey.SetValue(null, 1, RegistryValueKind.DWord); // 1 表示允许 SolidWorks 加载插件。
                addInKey.SetValue("Title", AddInTitle, RegistryValueKind.String); // 写入插件显示名称。
                addInKey.SetValue("Description", AddInDescription, RegistryValueKind.String); // 写入插件说明。
            }

            using (RegistryKey? startupKey = Registry.CurrentUser.CreateSubKey(
                $"Software\\SolidWorks\\AddInsStartup\\{{{AddInGuid}}}"))
            {
                startupKey?.SetValue(null, 1, RegistryValueKind.DWord); // 允许 SolidWorks 启动时自动加载当前插件。
            }
        }
    }
}

using SolidWorks.Interop.sldworks;
using System;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using Xarial.XCad.Base.Attributes;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// RightResourceLibrary.xaml 的交互逻辑
    /// </summary>
    [Title("CAD\\SOLIDWORKS资源库")] // 设置 SolidWorks 右侧任务面板标题，覆盖默认的控件类名。
    [Icon(typeof(Resource), nameof(Resource.沈阳铝镁院LOGO_NEW))] // 使用项目内置 Logo 作为任务面板图标。
    public partial class RightResourceLibrary : UserControl
    {
        /// <summary>通知插件主入口打开统一登录窗口。</summary>
        public event EventHandler? LoginRequested;

        public RightResourceLibrary()
        {
            SolidWorksFileLogger.Start("初始化 SolidWorks 右侧资源库控件");
            InitializeComponent();
            // 先初始化 WebView2，后续通过虚拟主机加载本地 HTML 和 GLB/GLTF，避免 file:// 路径访问失败。
            _ = InitializeThreeDPreviewAsync();
            // 控件刚创建时先按未登录状态显示遮罩，避免 XCAD 延迟触发认证初始化时业务页面先显示。
            SetAuthenticationState(false);
            // 版本号控件已迁移到“工具设置-系统设置”页面，使用迁移后的控件名称读取并更新版本信息。
            var versionText = FindName("SolidWorksAddInVersionText_设置") as TextBlock;
            if (versionText != null)
                versionText.Text = GetProjectVersions();
            // 先显示本地版本，随后通过服务器 API 更新服务器实际版本，避免启动时阻塞资源库界面。
            _ = LoadServerVersionAsync(versionText);

            // 控件全部完成初始化后再执行一次筛选，填充用户列表的初始数据源。
            ApplySolidWorksUserFilter();
            SolidWorksFileLogger.Complete("初始化 SolidWorks 右侧资源库控件");
        }

        private SolidWorksResourceApiService.ResourceDto _selectedSolidWorksResource;
        private Task _solidWorksThreeDPreviewInitializationTask;

        private void EnsureSolidWorksResourceApi()
        {
            if (_solidWorksResourceApi == null)
                _solidWorksResourceApi = new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
        }

        /// <summary>初始化 WebView2 三维预览宿主；运行环境没有 WebView2 时仅禁用三维预览。</summary>
        private async Task InitializeThreeDPreviewAsync()
        {
            if (_solidWorksThreeDPreviewInitializationTask != null)
            {
                await _solidWorksThreeDPreviewInitializationTask;
                return;
            }

            _solidWorksThreeDPreviewInitializationTask = InitializeThreeDPreviewHostsAsync();
            await _solidWorksThreeDPreviewInitializationTask;
        }

        private async Task InitializeThreeDPreviewHostsAsync()
        {
            try
            {
                await SW_ThreeDPreviewHost.EnsureCoreWebView2Async();
                SW_ThreeDPreviewHost.NavigateToString("<html><body style='background:#111;color:#ddd'>请选择 SolidWorks 资源后预览三维模型。</body></html>");
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Warning("初始化 SolidWorks 公用图元三维预览失败：" + ex.Message);
            }

            try
            {
                await SW_资源ThreeDPreviewHost.EnsureCoreWebView2Async();
                SW_资源ThreeDPreviewHost.NavigateToString("<html><body style='background:#111;color:#ddd'>请选择 SolidWorks 资源后预览三维模型。</body></html>");
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Warning("初始化 SolidWorks 管理员三维预览失败：" + ex.Message);
            }
        }

        /// <summary>读取当前已加载 SolidWorks 插件 DLL 的程序集版本号。</summary>
        private static string GetAddInVersion()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
            SolidWorksFileLogger.Info("读取 SolidWorks 插件版本：" + version + "。");
            return version;
        }

        /// <summary>显示当前加载插件及相关项目的统一版本，便于确认各组件是否来自同一发布包。</summary>
        private static string GetProjectVersions()
        {
            SolidWorksFileLogger.Start("读取平台版本信息");
            string versions = string.Join(System.Environment.NewLine, new[]
            {
                $"CAD 平台版本号：{ProjectVersionInfo.CadVersion}",
                $"SolidWorks 平台版本号：{GetAddInVersion()}",
                $"Shared 平台版本号：{ProjectVersionInfo.SharedVersion}",
                $"Server 平台版本号：{ProjectVersionInfo.ServerVersion}",
                $"Tray 平台版本号：{ProjectVersionInfo.TrayVersion}"
            });
            SolidWorksFileLogger.Complete("读取平台版本信息");
            return versions;
        }

        /// <summary>从服务器读取实际发布版本；服务器不可用时保留本地版本显示。</summary>
        private async Task LoadServerVersionAsync(TextBlock versionText)
        {
            if (versionText == null) return;
            try
            {
                SharedLoginSession session = new SharedLoginSessionStore().Load();
                SolidWorksAdministrationApiService api = new SolidWorksAdministrationApiService(session);
                string serverVersion = await api.GetConfigAsync("Version").ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(serverVersion))
                {
                    SolidWorksFileLogger.Warning("服务器版本配置为空，继续显示本地版本信息。" );
                    return;
                }

                string[] lines = versionText.Text.Split(new[] { System.Environment.NewLine }, StringSplitOptions.None);
                for (int index = 0; index < lines.Length; index++)
                {
                    if (lines[index].StartsWith("Server 平台版本号：", StringComparison.Ordinal))
                    {
                        lines[index] = "Server 平台版本号（服务器）：" + serverVersion.Trim();
                        break;
                    }
                }
                versionText.Text = string.Join(System.Environment.NewLine, lines);
                SolidWorksFileLogger.Info("已从服务器读取实际版本号；版本=" + serverVersion.Trim() + "。" );
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Warning("读取服务器实际版本号失败，继续显示本地版本信息：" + ex.Message);
            }
        }

        /// <summary>
        /// 获取或设置 SolidWorks 应用程序对象的引用。
        /// </summary>
        public ISldWorks? SW { get; internal set; }

        /// <summary>
        /// 更新资源库的登录状态；未登录时隐藏所有业务内容，避免未授权访问图元功能。
        /// </summary>
        public void SetAuthenticationState(bool authenticated)
        {
            SolidWorksFileLogger.Info("设置资源库认证状态；状态=" + (authenticated ? "已登录" : "未登录") + "。");
            // 使用 FindName 兼容旧版 WPF/XAML 生成器，避免新增控件名称未生成字段时阻断编译。
            var libraryTabControl = FindElement<UIElement>("LibraryTabControl");
            var loginRequiredOverlay = FindElement<UIElement>("LoginRequiredOverlay");
            var toolSettingsTab = FindElement<UIElement>("SWToolSettingsTab");
            var adminModuleTab = FindElement<UIElement>("SW_AdminModuleTab");
            var departmentUsersModuleTab = FindElement<UIElement>("SW_DepartmentUsersModuleTab");
            if (libraryTabControl != null)
                libraryTabControl.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
            if (loginRequiredOverlay != null)
            {
                loginRequiredOverlay.Visibility = authenticated ? Visibility.Collapsed : Visibility.Visible;
                Panel.SetZIndex(loginRequiredOverlay, authenticated ? 0 : 100);
            }
            if (toolSettingsTab != null)
                toolSettingsTab.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
            if (adminModuleTab != null)
                adminModuleTab.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
            if (departmentUsersModuleTab != null)
                departmentUsersModuleTab.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;

            SetDepartmentUserAuthentication(authenticated);
        }

        partial void SetDepartmentUserAuthentication(bool authenticated);

        private T FindElement<T>(string name) where T : UIElement
        {
            SolidWorksFileLogger.Info("查找资源库界面元素：" + name + "。");
            var element = FindName(name) as T;
            if (element != null)
                return element;

            return FindVisualChild<T>(this, name);
        }

        private static T FindVisualChild<T>(DependencyObject parent, string name) where T : UIElement
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T element && (element as FrameworkElement)?.Name == name)
                    return element;

                T nested = FindVisualChild<T>(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        /// <summary>点击“点击登录”链接，交由插件主入口显示统一登录窗口。</summary>
        private void OpenLoginButton_Click(object sender, RoutedEventArgs e)
        {
            SolidWorksFileLogger.Info("资源库登录按钮被点击。");
            LoginRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 处理 TabControl 的 SelectionChanged 事件。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SolidWorksFileLogger.Info("资源库标签页选择发生变化。");
            SolidWorksFileLogger.Info("资源库顶部标签页选择发生变化。");
            // 简单的空实现，避免 XAML 指定的事件处理器缺失导致的编译错误
            // 可在此处添加需要的逻辑
        }

    }  
}

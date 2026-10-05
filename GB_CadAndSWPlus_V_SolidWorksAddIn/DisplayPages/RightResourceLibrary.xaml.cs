using SolidWorks.Interop.sldworks;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GB_CadAndSWPlus_V.Shared.Services;
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
            InitializeComponent();
            // 控件刚创建时先按未登录状态显示遮罩，避免 XCAD 延迟触发认证初始化时业务页面先显示。
            SetAuthenticationState(false);
            var versionText = FindName("SolidWorksAddInVersionText") as TextBlock;
            if (versionText != null)
                versionText.Text = GetProjectVersions();

            // 控件全部完成初始化后再执行一次筛选，填充用户列表的初始数据源。
            ApplySolidWorksUserFilter();
        }

        /// <summary>读取当前已加载 SolidWorks 插件 DLL 的程序集版本号。</summary>
        private static string GetAddInVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "未知";
        }

        /// <summary>显示当前加载插件及相关项目的统一版本，便于确认各组件是否来自同一发布包。</summary>
        private static string GetProjectVersions()
        {
            return string.Join(System.Environment.NewLine, new[]
            {
                $"CAD 平台版本号：{ProjectVersionInfo.CadVersion}",
                $"SolidWorks 平台版本号：{GetAddInVersion()}",
                $"Shared 平台版本号：{ProjectVersionInfo.SharedVersion}",
                $"Server 平台版本号：{ProjectVersionInfo.ServerVersion}",
                $"Tray 平台版本号：{ProjectVersionInfo.TrayVersion}"
            });
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
            // 使用 FindName 兼容旧版 WPF/XAML 生成器，避免新增控件名称未生成字段时阻断编译。
            var libraryTabControl = FindElement<UIElement>("LibraryTabControl");
            var loginRequiredOverlay = FindElement<UIElement>("LoginRequiredOverlay");
            var toolSettingsTab = FindElement<UIElement>("SWToolSettingsTab");
            var adminModuleTab = FindElement<UIElement>("SWAdminModuleTab");
            var departmentUsersModuleTab = FindElement<UIElement>("SWDepartmentUsersModuleTab");
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
            LoginRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 处理 TabControl 的 SelectionChanged 事件。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 简单的空实现，避免 XAML 指定的事件处理器缺失导致的编译错误
            // 可在此处添加需要的逻辑
        }

    }  
}

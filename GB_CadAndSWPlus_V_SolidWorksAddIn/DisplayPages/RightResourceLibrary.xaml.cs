using SolidWorks.Interop.sldworks;
using System;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// RightResourceLibrary.xaml 的交互逻辑
    /// </summary>
    public partial class RightResourceLibrary : UserControl
    {
        /// <summary>通知插件主入口打开统一登录窗口。</summary>
        public event EventHandler? LoginRequested;

        public RightResourceLibrary()
        {
            InitializeComponent();
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
            var libraryTabControl = FindName("LibraryTabControl") as UIElement;
            var loginRequiredOverlay = FindName("LoginRequiredOverlay") as UIElement;
            if (libraryTabControl != null)
                libraryTabControl.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
            if (loginRequiredOverlay != null)
                loginRequiredOverlay.Visibility = authenticated ? Visibility.Collapsed : Visibility.Visible;
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

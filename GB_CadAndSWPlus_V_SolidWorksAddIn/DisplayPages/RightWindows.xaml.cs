using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// TaskPanel.xaml 的交互逻辑
    /// </summary>
    public partial class RightWindows : UserControl
    {
        public RightWindows()
        {
            InitializeComponent();
        }

        public ISldWorks Sw { get; set; }


        private void 管道_Click(object sender, RoutedEventArgs e)
        {
            // 1. 弹出法兰窗口
            ShowPelineWindow();

            // 2. 记录日志
            LogClick();

            // 3. 可选：更新界面状态
            UpdateUIState();
        }
        // 法兰按钮点击事件
        private void 法兰_Click(object sender, RoutedEventArgs e)
        {
            // 1. 弹出法兰窗口
            ShowFlangeWindow();

            // 2. 记录日志
            LogClick();

            // 3. 可选：更新界面状态
            UpdateUIState();
        }

        /// <summary>
        /// 显示法兰窗口
        /// </summary>
        private void ShowFlangeWindow()
        {
            try
            {
                if (Sw == null)
                {
                    return;
                }

                // 使用SolidWorks句柄创建窗口
                var flangeWindow = new FlangeWindow((IntPtr)Sw.IFrameObject().GetHWnd());
                flangeWindow.Owner = Window.GetWindow(this);
                flangeWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开法兰窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// 显示法兰窗口
        /// </summary>
        private void ShowPelineWindow()
        {
            try
            {
                if (Sw == null)
                {
                    return;
                }

                // 使用SolidWorks句柄创建窗口
                var pelineWindow = new PipelineWindow((IntPtr)Sw.IFrameObject().GetHWnd());
                pelineWindow.Owner = Window.GetWindow(this);
                pelineWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开法兰窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 日志记录
        private void LogClick()
        {
            // 实现日志记录逻辑
            //Logger.Log("法兰按钮被点击");
        }

        // 更新界面状态
        private void UpdateUIState()
        {
            // 可以禁用/启用按钮
            管道.IsEnabled = false;
            法兰.IsEnabled = false;

            // 延迟恢复
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1000);
                管道.IsEnabled = true;
                法兰.IsEnabled = true;
            });
        }
    }
}

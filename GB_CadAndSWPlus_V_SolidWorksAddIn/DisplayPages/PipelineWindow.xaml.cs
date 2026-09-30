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
    /// PipelineWindow.xaml 的交互逻辑
    /// </summary>
    public partial class PipelineWindow : Window
    {
        public PipelineWindow(IntPtr windowHandle)
        {
            InitializeComponent();

            #region 实现窗体的所有者为SolidWorks主窗体，使得窗体可以随着SolidWorks主窗体一起最小化和最大化，并且在SolidWorks主窗体最前端时，副窗体也在最前端
            var interopHelper = new System.Windows.Interop.WindowInteropHelper(this); //设置窗体的所有者为SolidWorks主窗体
            interopHelper.Owner = windowHandle;//当前的副窗体的所有者设置为SolidWorks主窗体
            #endregion
        }

    }
}

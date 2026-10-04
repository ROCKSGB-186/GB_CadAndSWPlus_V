using SolidWorks.Interop.sldworks;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// RightResourceLibrary.xaml 的交互逻辑
    /// </summary>
    public partial class RightResourceLibrary : UserControl
    {
        public RightResourceLibrary()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 获取或设置 SolidWorks 应用程序对象的引用。
        /// </summary>
        public ISldWorks? SW { get; internal set; }

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

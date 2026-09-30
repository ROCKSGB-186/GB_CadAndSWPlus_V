using SolidWorks.Interop.sldworks;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// SolidWorks 插件的统一 WPF 页面入口。
    /// 页面由 Frame 承载具体业务页面，便于后续增加标准、属性和设置页面。
    /// </summary>
    public partial class AddInMainPage : Page
    {
        public AddInMainPage(ISldWorks? solidWorks, SwUserSession? userSession = null)
        {
            InitializeComponent();
            Sw = solidWorks;
            UserSession = userSession;
            ContentFrame.Navigate(new PipelinePage(Sw));
        }

        /// <summary>
        /// 当前 SolidWorks 应用对象，供子页面执行模型操作时使用。
        /// </summary>
        public ISldWorks? Sw { get; }

        public SwUserSession? UserSession { get; }

        private void Pipeline_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new PipelinePage(Sw));
        }

        private void Flange_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new FlangePage(Sw, UserSession));
        }

        private void Department_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new DepartmentPage(UserSession));
        }

        private void Category_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new CategoryPage(UserSession));
        }
    }
}

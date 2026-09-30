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
    /// FeatureManagerTab.xaml 的交互逻辑
    /// </summary>
    public partial class FeatureManagerTab : UserControl
    {
        public FeatureManagerTab()
        {
            InitializeComponent();
        }

        public ISldWorks Sw { get; set; }
    }
}

using SolidWorks.Interop.sldworks;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// 法兰业务页面。后续可在这里接入服务器标准匹配、属性编辑和 SolidWorks 模型插入逻辑。
    /// </summary>
    public partial class FlangePage : Page
    {
        public FlangePage(ISldWorks? solidWorks, SwUserSession? userSession = null)
        {
            InitializeComponent();
            Sw = solidWorks;
            UserSession = userSession;
        }

        public ISldWorks? Sw { get; }

        public SwUserSession? UserSession { get; }

        private async void InsertGraphic_Click(object sender, RoutedEventArgs e)
        {
            // 第一步：读取用户选择的服务器图元 ID，避免将页面输入直接传给 SolidWorks API。
            if (!int.TryParse(GraphicIdTextBox.Text, out int graphicId) || graphicId <= 0)
            {
                SetStatus("请输入有效的图元 ID。", true);
                return;
            }

            if (Sw == null)
            {
                SetStatus("SolidWorks 当前未连接。", true);
                return;
            }

            try
            {
                SetStatus("正在下载图元并插入装配体……", false);
                SwApiSettings settings = UserSession?.Settings ?? SwUserSession.Load().Settings;
                SwApiClient apiClient = new SwApiClient(settings);
                var insertService = new SwGraphicInsertService(
                    Sw,
                    new SwGraphicService(apiClient),
                    new SwInstanceClientService(apiClient));

                SwGraphicInsertResult result = await insertService.InsertAsync(
                    graphicId,
                    string.IsNullOrWhiteSpace(LocalFallbackPathTextBox.Text) ? null : LocalFallbackPathTextBox.Text.Trim());
                SetStatus(
                    result.RegistrationSucceeded
                        ? "图元已插入并完成服务器实例登记：" + result.ComponentName
                        : "图元已插入，但服务器登记未成功：" + result.RegistrationMessage,
                    !result.RegistrationSucceeded);
            }
            catch (Exception ex)
            {
                SetStatus("图元插入失败：" + ex.Message, true);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // 页面取消只清理本次输入，不关闭 SolidWorks 或删除已有组件。
            GraphicIdTextBox.Text = string.Empty;
            LocalFallbackPathTextBox.Text = string.Empty;
            SetStatus(string.Empty, false);
        }

        private void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                isError ? System.Windows.Media.Colors.Firebrick : System.Windows.Media.Colors.DarkGreen);
        }
    }
}

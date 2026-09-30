using SolidWorks.Interop.sldworks;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>
    /// 管道业务页面。后续可在这里接入服务器管道字段、规范匹配和 SolidWorks 插入逻辑。
    /// </summary>
    public partial class PipelinePage : Page
    {
        public PipelinePage(ISldWorks? solidWorks)
        {
            InitializeComponent();
            Sw = solidWorks;
        }

        public ISldWorks? Sw { get; }

        private async void OpenPlatformAssembly_Click(object sender, RoutedEventArgs e)
        {
            if (Sw == null)
            {
                SetAssemblyStatus("SolidWorks 尚未连接。");
                return;
            }

            try
            {
                SetAssemblyStatus("正在获取模板并打开平台装配体……");
                var settings = Configuration.SwUserSession.Load().Settings;
                var apiClient = new SwApiClient(settings);
                var cache = new SwTemplateCacheService(apiClient);
                var workflow = new SwPlatformAssemblyService(Sw, cache);
                var templateClient = new SwPlatformTemplateClientService(apiClient);
                SwPlatformTemplateResponse? template = null;
                try
                {
                    // 第一步：在线查询服务器模板；服务器不可用时继续执行本地回退。
                    template = await templateClient.GetAsync("GB-PIPELINE").ConfigureAwait(true);
                }
                catch (Exception templateException)
                {
                    // 第二步：记录在线查询失败，但不阻断 Resources 和本地缓存路径。
                    SetAssemblyStatus("服务器模板不可用，正在尝试本地模板：" + templateException.Message);
                }
                string outputDirectory = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    "GB_CADPLUS",
                    "SolidWorksAssemblies");
                string? assemblyPath = await workflow.OpenOrCreateAsync(
                    "GB-PIPELINE",
                    template?.Success == true ? template.DownloadPath : null,
                    null,
                    outputDirectory).ConfigureAwait(true);
                SetAssemblyStatus(assemblyPath == null ? "未找到可用的平台装配体模板。" : "已打开：" + assemblyPath);
            }
            catch (Exception ex)
            {
                SetAssemblyStatus("平台装配体操作失败：" + ex.Message);
            }
        }

        private void SetAssemblyStatus(string message)
        {
            if (FindName("AssemblyStatusText") is System.Windows.Controls.TextBlock textBlock)
                textBlock.Text = message;
        }
    }
}

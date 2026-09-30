using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using System;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    public partial class DepartmentPage : Page
    {
        private readonly SwUserSession? _session;
        private readonly DepartmentService _departmentService = new DepartmentService();

        public DepartmentPage(SwUserSession? session)
        {
            InitializeComponent();
            _session = session;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            if (_session == null || !_session.IsSignedIn)
            {
                StatusTextBlock.Text = "当前没有有效登录会话。";
                return;
            }

            try
            {
                IsEnabled = false;
                StatusTextBlock.Text = "正在加载服务器部门...";
                DepartmentListResponse response = await _departmentService.GetAsync(_session.Settings);
                if (!response.Success)
                {
                    StatusTextBlock.Text = string.IsNullOrWhiteSpace(response.Message) ? "部门查询失败。" : response.Message;
                    return;
                }

                response.Departments = response.Departments ?? new System.Collections.Generic.List<DepartmentDto>();
                DepartmentListView.ItemsSource = response.Departments;
                StatusTextBlock.Text = response.Departments.Count == 0
                    ? "服务器连接成功，但当前没有可用部门。"
                    : $"已加载 {response.Departments.Count} 个部门。数据来自统一服务器。";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = ex.Message;
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }
}

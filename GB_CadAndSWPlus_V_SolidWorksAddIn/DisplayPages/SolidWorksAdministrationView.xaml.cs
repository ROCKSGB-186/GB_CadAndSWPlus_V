using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    /// <summary>SolidWorks 端与 CAD 主页面对应的工具设置、管理员和部门/人员模块。</summary>
    public partial class SolidWorksAdministrationView : UserControl
    {
        /// <summary>指定当前实例显示的模块：工具设置、管理员模块或部门\人员模块。</summary>
        public string Module { get; set; } = "工具设置";

        private SharedLoginSession _session = new SharedLoginSession();
        private SolidWorksAdministrationApiService _api;
        private List<SolidWorksDepartmentModel> _departments = new List<SolidWorksDepartmentModel>();
        private bool _isAdministrator;

        public SolidWorksAdministrationView()
        {
            SolidWorksFileLogger.Start("初始化 SolidWorks 管理视图");
            InitializeComponent();
            SetAuthenticationState(false);
            SolidWorksFileLogger.Complete("初始化 SolidWorks 管理视图");
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            SolidWorksFileLogger.Info("SolidWorks 管理视图加载完成。");
            SelectModule();
        }

        private void SelectModule()
        {
            SolidWorksFileLogger.Info("选择 SolidWorks 管理视图模块：" + Module + "。");
            if (AdministrationTabControl == null) { SolidWorksFileLogger.Skipped("选择 SolidWorks 管理视图模块", "标签控件尚未初始化"); return; }
            AdministrationTabControl.SelectedIndex = Module == "管理员模块" ? 1 : Module == "部门\\人员模块" ? 2 : 0;
        }

        /// <summary>设置统一登录上下文并刷新管理数据。</summary>
        public async void SetAuthenticationState(bool authenticated)
        {
            SolidWorksFileLogger.Info("设置 SolidWorks 管理视图认证状态；状态=" + (authenticated ? "已登录" : "未登录") + "。");
            if (!authenticated)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            _session = new SharedLoginSessionStore().Load();
            _api = new SolidWorksAdministrationApiService(_session);
            _isAdministrator = IsAdministrator(_session.Username);
            Visibility = Visibility.Visible;
            管理员模块Tab.Visibility = _isAdministrator ? Visibility.Visible : Visibility.Collapsed;
            部门人员模块Tab.Visibility = _isAdministrator ? Visibility.Visible : Visibility.Collapsed;
            if (_isAdministrator) await RefreshAsync();
            else SettingsStatusText.Text = "当前账号没有管理员权限，仅可使用工具设置。";
        }

        private static bool IsAdministrator(string username)
        {
            string value = (username ?? string.Empty).Trim().ToLowerInvariant();
            return value == "sa" || value == "admin" || value == "root" || value == "sysdba";
        }

        private async Task RefreshAsync()
        {
            SolidWorksFileLogger.Start("刷新 SolidWorks 管理数据");
            try
            {
                SolidWorksDepartmentResponse response = await _api.GetDepartmentsAsync();
                _departments = response.Departments ?? new List<SolidWorksDepartmentModel>();
                AdminDepartmentsGrid.ItemsSource = _departments;
                DepartmentsGrid.ItemsSource = _departments;
                SettingsStatusText.Text = "管理数据已刷新：部门数量=" + _departments.Count;
                SolidWorksFileLogger.Complete("刷新 SolidWorks 管理数据；部门数量=" + _departments.Count);
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Failed("刷新 SolidWorks 管理数据", ex);
                SolidWorksFileLogger.Warning("SolidWorks 管理数据刷新失败：" + ex.Message);
                SettingsStatusText.Text = "管理数据刷新失败：" + ex.Message;
            }
        }

        private async void RefreshDepartments_Click(object sender, RoutedEventArgs e)
        {
            SolidWorksFileLogger.Info("点击刷新 SolidWorks 部门按钮。");
            await RefreshAsync();
        }

        private async void DepartmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SolidWorksFileLogger.Info("SolidWorks 部门选择发生变化。");
            SolidWorksDepartmentModel department = DepartmentsGrid.SelectedItem as SolidWorksDepartmentModel;
            if (department == null || _api == null) return;
            try
            {
                SolidWorksUserResponse response = await _api.GetUsersAsync(department.Id);
                UsersGrid.ItemsSource = response.Users ?? new List<SolidWorksUserModel>();
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Warning("SolidWorks 人员列表刷新失败：" + ex.Message);
                UsersGrid.ItemsSource = null;
            }
        }

        private async void AddDepartment_Click(object sender, RoutedEventArgs e)
        {
            SolidWorksFileLogger.Start("新增 SolidWorks 管理部门");
            if (!_isAdministrator || _api == null) { SolidWorksFileLogger.Skipped("新增 SolidWorks 管理部门", "当前账号无管理员权限或 API 未初始化"); return; }
            string name = PromptForDepartmentName();
            if (string.IsNullOrWhiteSpace(name)) { SolidWorksFileLogger.Skipped("新增 SolidWorks 管理部门", "用户取消或未填写部门名称"); return; }
            SolidWorksMutationResult result = await _api.AddDepartmentAsync(name.Trim(), name.Trim(), string.Empty, _departments.Count + 1);
            MessageBox.Show(result.Success ? "部门新增成功。" : "部门新增失败：" + result.Message, "部门管理", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) await RefreshAsync();
        }

        private static string PromptForDepartmentName()
        {
            SolidWorksFileLogger.Info("显示 SolidWorks 新增部门输入窗口。");
            var dialog = new Window
            {
                Title = "新增部门",
                Width = 300,
                Height = 145,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };
            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = "请输入部门名称：" });
            var input = new TextBox { Margin = new Thickness(0, 6, 0, 8) };
            panel.Children.Add(input);
            var ok = new Button { Content = "确定", Width = 70, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
            ok.Click += (sender, args) => dialog.DialogResult = true;
            panel.Children.Add(ok);
            dialog.Content = panel;
            return dialog.ShowDialog() == true ? input.Text.Trim() : string.Empty;
        }

        private async void DeleteDepartment_Click(object sender, RoutedEventArgs e)
        {
            SolidWorksFileLogger.Start("删除 SolidWorks 管理部门");
            SolidWorksDepartmentModel department = AdminDepartmentsGrid.SelectedItem as SolidWorksDepartmentModel;
            if (!_isAdministrator || department == null || _api == null) { SolidWorksFileLogger.Skipped("删除 SolidWorks 管理部门", "权限、API 或部门选择不满足条件"); return; }
            if (MessageBox.Show("确认删除部门“" + department.DisplayName + "”？", "部门管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { SolidWorksFileLogger.Skipped("删除 SolidWorks 管理部门", "用户取消确认"); return; }
            SolidWorksMutationResult result = await _api.DeleteDepartmentAsync(department.Id);
            MessageBox.Show(result.Success ? "部门删除成功。" : "部门删除失败：" + result.Message, "部门管理", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) await RefreshAsync();
        }

        private async void SaveSystemConfig_Click(object sender, RoutedEventArgs e)
        {
            SolidWorksFileLogger.Start("保存 SolidWorks 系统配置");
            if (_api == null) { SettingsStatusText.Text = "请先登录统一平台。"; SolidWorksFileLogger.Skipped("保存 SolidWorks 系统配置", "尚未登录"); return; }
            try
            {
                await _api.SetConfigAsync("server_version", ServerVersionTextBox.Text);
                await _api.SetConfigAsync("client_version", ClientVersionTextBox.Text);
                SettingsStatusText.Text = "系统配置保存成功。";
                SolidWorksFileLogger.Complete("保存 SolidWorks 系统配置");
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Failed("保存 SolidWorks 系统配置", ex);
                SolidWorksFileLogger.Warning("SolidWorks 系统配置保存失败：" + ex.Message);
                SettingsStatusText.Text = "系统配置保存失败：" + ex.Message;
            }
        }
    }
}

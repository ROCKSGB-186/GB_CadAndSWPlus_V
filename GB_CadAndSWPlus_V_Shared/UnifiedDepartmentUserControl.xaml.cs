using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace GB_CadAndSWPlus_V.Shared
{
    /// <summary>CAD 与 SolidWorks 共用的部门/人员页面控件。</summary>
    public partial class UnifiedDepartmentUserControl : UserControl
    {
        private UnifiedDepartmentUserApiService _api;
        private List<UnifiedDepartmentModel> _departments = new List<UnifiedDepartmentModel>();

        public UnifiedDepartmentUserControl()
        {
            InitializeComponent();
            IsEnabled = false;
        }

        /// <summary>设置统一登录会话；两个平台均通过此方法进入同一套部门/人员逻辑。</summary>
        public void SetSession(SharedLoginSession session, bool isAdministrator)
        {
            _api = session == null || !isAdministrator ? null : new UnifiedDepartmentUserApiService(session);
            IsEnabled = _api != null;
            Visibility = _api == null ? Visibility.Collapsed : Visibility.Visible;
            if (_api != null) RefreshDepartments();
        }

        private async void RefreshDepartments()
        {
            try
            {
                UnifiedDepartmentListResponse response = await _api.GetDepartmentsAsync();
                _departments = response.Departments ?? new List<UnifiedDepartmentModel>();
                SWDepartmentsGrid.ItemsSource = _departments;
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取部门列表失败：" + ex.Message, "部门管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void SWDepartmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var department = SWDepartmentsGrid.SelectedItem as UnifiedDepartmentModel;
            if (department == null || _api == null) return;
            try
            {
                List<UnifiedUserModel> users = await _api.GetUsersAsync(department.Id);
                SWUsersGrid.ItemsSource = users ?? new List<UnifiedUserModel>();
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取人员列表失败：" + ex.Message, "人员管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SWRefreshDepartmentsButton_Click(object sender, RoutedEventArgs e) => RefreshDepartments();

        private void SWAddDepartmentButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("新增部门请通过 CAD 或统一管理页面完成；此控件使用相同的统一部门数据源。", "部门管理", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void SWDeleteDepartmentButton_Click(object sender, RoutedEventArgs e)
        {
            var department = SWDepartmentsGrid.SelectedItem as UnifiedDepartmentModel;
            if (department == null || _api == null) return;
            if (MessageBox.Show("确认删除部门“" + department.DisplayName + "”？", "部门管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                UnifiedDepartmentMutationResponse response = await _api.DeleteDepartmentAsync(department.Id);
                if (!response.Success) throw new InvalidOperationException(response.Message);
                RefreshDepartments();
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除部门失败：" + ex.Message, "部门管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

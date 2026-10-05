using System;
using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    public partial class RightResourceLibrary
    {
        private UnifiedDepartmentUserApiService _departmentUserApi;
        private List<UnifiedDepartmentModel> _solidWorksDepartments = new List<UnifiedDepartmentModel>();
        private List<UnifiedUserModel> _solidWorksUsers = new List<UnifiedUserModel>();
        private bool _solidWorksAdministrator;

        partial void SetDepartmentUserAuthentication(bool authenticated)
        {
            _solidWorksAdministrator = authenticated && IsSolidWorksAdministrator();
            _departmentUserApi = null;
            if (authenticated)
            {
                SharedLoginSession session = new SharedLoginSessionStore().Load();
                _departmentUserApi = new UnifiedDepartmentUserApiService(session);
                _ = RefreshSolidWorksDepartmentsAsync();
            }
            else
            {
                DepartmentsGrid.ItemsSource = null;
                UsersGrid.ItemsSource = null;
            }
        }

        private bool IsSolidWorksAdministrator()
        {
            string username = new SharedLoginSessionStore().Load().Username ?? string.Empty;
            username = username.Trim().ToLowerInvariant();
            return username == "sa" || username == "admin" || username == "root" || username == "sysdba";
        }

        private async Task RefreshSolidWorksDepartmentsAsync()
        {
            if (_departmentUserApi == null) return;
            try
            {
                UnifiedDepartmentListResponse response = await _departmentUserApi.GetDepartmentsAsync();
                _solidWorksDepartments = response.Departments ?? new List<UnifiedDepartmentModel>();
                DepartmentsGrid.ItemsSource = _solidWorksDepartments;
                TxtStatus.Text = "已刷新部门，共 " + _solidWorksDepartments.Count + " 个。";
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("SolidWorks 部门刷新失败：" + ex.Message);
                TxtStatus.Text = "部门刷新失败：" + ex.Message;
            }
        }

        private async Task RefreshSolidWorksUsersAsync()
        {
            UnifiedDepartmentModel department = DepartmentsGrid.SelectedItem as UnifiedDepartmentModel;
            if (_departmentUserApi == null || department == null) return;
            try
            {
                UnifiedUserListResponse response = await _departmentUserApi.GetUsersAsync(department.Id);
                _solidWorksUsers = response.Users ?? new List<UnifiedUserModel>();
                ApplySolidWorksUserFilter();
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("SolidWorks 人员刷新失败：" + ex.Message);
                UsersGrid.ItemsSource = null;
                TxtStatus.Text = "人员刷新失败：" + ex.Message;
            }
        }

        private void ApplySolidWorksUserFilter()
        {
            // XAML 初始化期间 TextChanged 可能早于 UsersGrid 完成初始化而触发。
            if (TxtSearchUser == null || UsersGrid == null)
                return;

            string keyword = TxtSearchUser.Text == "输入用户名搜索/分配" ? string.Empty : TxtSearchUser.Text.Trim();
            UsersGrid.ItemsSource = string.IsNullOrWhiteSpace(keyword)
                ? _solidWorksUsers
                : _solidWorksUsers.Where(user => (user.Username + " " + user.RealName + " " + user.Phone).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private async Task ExecuteSolidWorksMutationAsync(Func<Task<bool>> action, string successMessage)
        {
            try
            {
                if (await action())
                {
                    TxtStatus.Text = successMessage;
                    await RefreshSolidWorksDepartmentsAsync();
                    await RefreshSolidWorksUsersAsync();
                }
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("SolidWorks 部门/人员操作失败：" + ex.Message);
                MessageBox.Show("操作失败：" + ex.Message, "部门/人员管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        /// <summary>手动复制的 CAD 页面事件入口。CAD 专属操作在 SolidWorks 中暂不直接执行，统一记录提示避免误操作。</summary>
        private void SWCopiedCadActionNotAvailable(string action)
        {
            Services.SolidWorksFileLogger.Info("SolidWorks 页面操作入口已接收；当前操作未执行 CAD 专属逻辑：" + action);
        }

        private void AddFile_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(AddFile_Btn_Click));
        private void AddSubcategory_MenuItem_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(AddSubcategory_MenuItem_Click));
        private void ApplyProperties_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(ApplyProperties_Btn_Click));
        private async void BtnAddDept_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null) return;
            DepartmentEditorResult result = PromptDepartment(null, "新增部门");
            if (result == null) return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.AddDepartmentAsync(result.Name, result.DisplayName, result.Description, result.SortOrder)).Success, "部门新增成功。");
        }

        private async void BtnAddUserManaged_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) return;
            UserEditorResult result = PromptUser(null, "新增人员");
            if (result == null) return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.AddUserAsync(result.Username, result.Password, department.Id, department.DisplayName, result.Role, result.IsActive, result.RealName, result.Gender, result.Phone, result.Email)).Success, "人员新增成功。");
        }

        private async void BtnAssignUser_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) return;
            string username = TxtSearchUser.Text.Trim();
            if (string.IsNullOrWhiteSpace(username) || username == "输入用户名搜索/分配") return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.AssignUserToDepartmentAsync(username, department.Id)).Success, "用户分配成功。");
        }

        private async void BtnDeleteDept_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) return;
            if (MessageBox.Show("确认删除部门“" + department.DisplayName + "”？", "部门管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.DeleteDepartmentAsync(department.Id)).Success, "部门删除成功。");
        }

        private async void BtnDeleteUserManaged_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(UsersGrid.SelectedItem is UnifiedUserModel user)) return;
            if (MessageBox.Show("确认删除用户“" + user.Username + "”？", "人员管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.DeleteUserAsync(user.Id)).Success, "人员删除成功。");
        }

        private async void BtnEditDept_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) return;
            DepartmentEditorResult result = PromptDepartment(department, "编辑部门");
            if (result == null) return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.UpdateDepartmentAsync(department.Id, result.Name, result.DisplayName, result.Description, result.SortOrder, null, result.IsActive)).Success, "部门修改成功。");
        }

        private async void BtnEditUserManaged_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(UsersGrid.SelectedItem is UnifiedUserModel user)) return;
            UserEditorResult result = PromptUser(user, "编辑人员");
            if (result == null) return;
            UnifiedDepartmentModel department = DepartmentsGrid.SelectedItem as UnifiedDepartmentModel;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.UpdateUserAsync(user.Id, result.Username, result.Role, result.IsActive, department?.Id, department?.DisplayName ?? string.Empty, result.Password, result.RealName, result.Gender, result.Phone, result.Email)).Success, "人员修改成功。");
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshSolidWorksUsersAsync();
        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null) return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.SyncDepartmentsFromCategoriesAsync()).Success, "部门同步成功。");
        }
        private void CheckBoxUseDPath_Checked(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(CheckBoxUseDPath_Checked));
        private void CheckBoxUseDPath_Unchecked(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(CheckBoxUseDPath_Unchecked));
        private void Delete_MenuItem_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(Delete_MenuItem_Click));
        private void DeleteGraphic_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(DeleteGraphic_Btn_Click));
        private void Edit_MenuItem_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(Edit_MenuItem_Click));
        private void ImportFromSelection_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(ImportFromSelection_Btn_Click));
        private void LoadCadDatabase_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(LoadCadDatabase_Btn_Click));
        private void LoadSwDatabase_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(LoadSwDatabase_Btn_Click));
        private void NewCategory_MenuItem_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(NewCategory_MenuItem_Click));
        private void PreviewImage_Loaded(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(PreviewImage_Loaded));
        private void ResetToInitial_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(ResetToInitial_Btn_Click));
        private void SelectFile_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(SelectFile_Btn_Click));
        private void SelectViewImage_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(SelectViewImage_Btn_Click));
        private void StroageFileDataGrid_Loaded(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(StroageFileDataGrid_Loaded));
        private void UploadClient_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(UploadClient_Btn_Click));
        private void 保存图层字典_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(保存图层字典_Click));
        private void 查看日志文件夹按钮_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(查看日志文件夹按钮_Click));
        private void 打开最新日志按钮_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(打开最新日志按钮_Click));
        private void 当前图纸图层_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(当前图纸图层_Click));
        private void 导出规范_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导出规范_Btn_Click));
        private void 导出模板_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导出模板_Btn_Click));
        private void 导入规范_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导入规范_Btn_Click));
        private void 导入模板_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导入模板_Btn_Click));
        private void 规范树右键按下(object sender, MouseButtonEventArgs e) => SWCopiedCadActionNotAvailable(nameof(规范树右键按下));
        private void 规范树右键菜单_Opened(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(规范树右键菜单_Opened));
        private void 加载标准图层字典_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(加载标准图层字典_Click));
        private void 加载个人图层字典_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(加载个人图层字典_Click));
        private void 加载显示日志按钮_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(加载显示日志按钮_Click));
        private void 删除规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(删除规范库_Click));
        private void 删除选中行_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(删除选中行_Click));
        private void 刷新规范_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(刷新规范_Click));
        private void 刷新架构树按钮_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(刷新架构树按钮_Click));
        private void 添加一行_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(添加一行_Click));
        private void 添加主规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(添加主规范库_Click));
        private void 添加子规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(添加子规范库_Click));
        private void 同步图元_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(同步图元_Click));
        private void 下载最新版客户端_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(下载最新版客户端_Click));
        private void 修改规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(修改规范库_Click));
        private void 应用设置按钮_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(应用设置按钮_Click));
        private void 右键导出规范_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键导出规范_Click));
        private void 右键导入规范_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键导入规范_Click));
        private void 右键删除规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键删除规范库_Click));
        private void 右键删除规范细分_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键删除规范细分_Click));
        private void 右键上移规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键上移规范库_Click));
        private void 右键刷新架构_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键刷新架构_Click));
        private void 右键添加主规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键添加主规范库_Click));
        private void 右键添加子规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键添加子规范库_Click));
        private void 右键下移规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键下移规范库_Click));
        private void 右键修改规范库_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键修改规范库_Click));
        private void 右键移动规范位置_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键移动规范位置_Click));
        private void 右键展开折叠规范_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键展开折叠规范_Click));
        private void 右键重命名规范_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(右键重命名规范_Click));
        private void 展开_折叠规范_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(展开_折叠规范_Click));
        private void 展开_折叠架构_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(展开_折叠架构_Click));
        private void 指定路径按键_OnClick(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(指定路径按键_OnClick));
        private void 上传客户端_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(上传客户端_Btn_Click));
        private void StroageFileDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SWCopiedCadActionNotAvailable(nameof(StroageFileDataGrid_MouseDoubleClick));
        private void StroageFileDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(StroageFileDataGrid_SelectedCellsChanged));
        private void StroageFileDataGrid_TargetUpdated(object sender, DataTransferEventArgs e) => SWCopiedCadActionNotAvailable(nameof(StroageFileDataGrid_TargetUpdated));
        private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => SWCopiedCadActionNotAvailable(nameof(CategoryTreeView_SelectedItemChanged));
        private void SpecificationTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => SWCopiedCadActionNotAvailable(nameof(SpecificationTreeView_SelectedItemChanged));
        private async void DepartmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => await RefreshSolidWorksUsersAsync();

        private void TxtSearchUser_TextChanged(object sender, TextChangedEventArgs e) => ApplySolidWorksUserFilter();

        private sealed class DepartmentEditorResult
        {
            public string Name;
            public string DisplayName;
            public string Description;
            public int SortOrder;
            public bool IsActive;
        }

        private sealed class UserEditorResult
        {
            public string Username;
            public string Password;
            public string RealName;
            public string Gender;
            public string Phone;
            public string Email;
            public string Role;
            public bool IsActive;
        }

        private static DepartmentEditorResult PromptDepartment(UnifiedDepartmentModel source, string title)
        {
            var name = new TextBox { Text = source?.Name ?? string.Empty };
            var displayName = new TextBox { Text = source?.DisplayName ?? string.Empty };
            var description = new TextBox { Text = source?.Description ?? string.Empty };
            var sortOrder = new TextBox { Text = (source?.SortOrder ?? 0).ToString() };
            var dialog = CreateEditorWindow(title, new[] { Tuple.Create("名称", (Control)name), Tuple.Create("显示名", (Control)displayName), Tuple.Create("说明", (Control)description), Tuple.Create("排序", (Control)sortOrder) });
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(name.Text)) return null;
            return new DepartmentEditorResult { Name = name.Text.Trim(), DisplayName = string.IsNullOrWhiteSpace(displayName.Text) ? name.Text.Trim() : displayName.Text.Trim(), Description = description.Text.Trim(), SortOrder = int.TryParse(sortOrder.Text, out int value) ? value : 0, IsActive = source == null || source.IsActive };
        }

        private static UserEditorResult PromptUser(UnifiedUserModel source, string title)
        {
            var username = new TextBox { Text = source?.Username ?? string.Empty };
            var password = new PasswordBox();
            var realName = new TextBox { Text = source?.RealName ?? string.Empty };
            var role = new TextBox { Text = source?.Role ?? "user" };
            var phone = new TextBox { Text = source?.Phone ?? string.Empty };
            var email = new TextBox { Text = source?.Email ?? string.Empty };
            var dialog = CreateEditorWindow(title, new[] { Tuple.Create("账号", (Control)username), Tuple.Create("密码", (Control)password), Tuple.Create("姓名", (Control)realName), Tuple.Create("角色", (Control)role), Tuple.Create("电话", (Control)phone), Tuple.Create("邮箱", (Control)email) });
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(username.Text)) return null;
            return new UserEditorResult { Username = username.Text.Trim(), Password = password.Password, RealName = realName.Text.Trim(), Role = string.IsNullOrWhiteSpace(role.Text) ? "user" : role.Text.Trim(), Phone = phone.Text.Trim(), Email = email.Text.Trim(), IsActive = source == null || source.IsActive };
        }

        private static Window CreateEditorWindow(string title, IEnumerable<Tuple<string, Control>> fields)
        {
            var dialog = new Window { Title = title, Width = 360, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize };
            var panel = new StackPanel { Margin = new Thickness(12) };
            foreach (Tuple<string, Control> field in fields) { panel.Children.Add(new TextBlock { Text = field.Item1 }); panel.Children.Add(field.Item2); field.Item2.Margin = new Thickness(0, 2, 0, 6); }
            var ok = new Button { Content = "确定", Width = 70, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
            ok.Click += (sender, args) => dialog.DialogResult = true;
            panel.Children.Add(ok); dialog.Content = panel; return dialog;
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    public partial class RightResourceLibrary
    {
        /// <summary>手动复制的 CAD 页面事件入口。CAD 专属操作在 SolidWorks 中暂不直接执行，统一记录提示避免误操作。</summary>
        private void SWCopiedCadActionNotAvailable(string action)
        {
            Services.SolidWorksFileLogger.Info("SolidWorks 页面操作入口已接收；当前操作未执行 CAD 专属逻辑：" + action);
        }

        private void AddFile_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(AddFile_Btn_Click));
        private void AddSubcategory_MenuItem_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(AddSubcategory_MenuItem_Click));
        private void ApplyProperties_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(ApplyProperties_Btn_Click));
        private void BtnAddDept_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnAddDept_Click));
        private void BtnAddUserManaged_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnAddUserManaged_Click));
        private void BtnAssignUser_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnAssignUser_Click));
        private void BtnDeleteDept_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnDeleteDept_Click));
        private void BtnDeleteUserManaged_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnDeleteUserManaged_Click));
        private void BtnEditDept_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnEditDept_Click));
        private void BtnEditUserManaged_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnEditUserManaged_Click));
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnRefresh_Click));
        private void BtnSync_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(BtnSync_Click));
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
        private void DepartmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(DepartmentsGrid_SelectionChanged));
    }
}

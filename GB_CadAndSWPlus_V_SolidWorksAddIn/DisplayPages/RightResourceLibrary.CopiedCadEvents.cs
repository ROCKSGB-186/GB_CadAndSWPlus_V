using System;
using System.Runtime.InteropServices;
using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    public partial class RightResourceLibrary
    {
        private UnifiedDepartmentUserApiService _departmentUserApi;
        private SolidWorksCategoryApiService _solidWorksCategoryApi;
        private List<SolidWorksCategoryNode> _solidWorksCategoryNodes = new List<SolidWorksCategoryNode>();
        private SolidWorksCategoryNode _selectedSolidWorksCategoryNode;
        private List<UnifiedDepartmentModel> _solidWorksDepartments = new List<UnifiedDepartmentModel>();
        private List<UnifiedUserModel> _solidWorksUsers = new List<UnifiedUserModel>();
        private bool _solidWorksAdministrator;
        private SolidWorksResourceApiService _solidWorksResourceApi;
        private readonly SolidWorksLocalCacheService _solidWorksLocalCache = new SolidWorksLocalCacheService();
        private List<SolidWorksResourceApiService.ElementDto> _solidWorksElementsByCategory = new List<SolidWorksResourceApiService.ElementDto>();
        private SolidWorksResourceApiService.ElementDto _selectedSolidWorksElement;
        private bool _loadingSolidWorksPublicResource;
        private Point _solidWorksButtonDragStartPoint;
        private object _drawingMouseObject;
        private DMouseEvents_MouseSelectNotifyEventHandler _drawingMouseSelectHandler;
        private DMouseEvents_MouseLBtnDownNotifyEventHandler _drawingMouseDownHandler;
        private DMouseEvents_MouseMoveNotifyEventHandler _drawingMouseMoveHandler;
        private DMouseEvents_MouseRBtnDownNotifyEventHandler _drawingMouseCancelHandler;
        private TaskCompletionSource<Point?> _drawingInsertPointSource;
        private Component2 _assemblyPreviewComponent;
        private ModelDoc2 _assemblyPreviewModel;
        private string _assemblyPreviewPath;
        private string _assemblyPreviewBaseName;
        private DispatcherTimer _assemblyPlacementTimer;
        private bool _assemblyPlacementLastLeftButtonDown;
        private bool _assemblyPlacementLastRightButtonDown;

        partial void SetDepartmentUserAuthentication(bool authenticated)
        {
            Services.SolidWorksFileLogger.Info("设置 SolidWorks 部门/人员模块认证状态；状态=" + (authenticated ? "已登录" : "未登录") + "。");
            if (!authenticated)
                CancelDrawingInsertPointSelection("用户退出登录");
            _solidWorksAdministrator = authenticated && IsSolidWorksAdministrator();
            if (authenticated && !_solidWorksAdministrator)
                TxtStatus.Text = "当前登录账号不是管理员，无法加载统一构件和执行 SolidWorks 管理操作。请使用 sa、admin 或 SYSDBA 登录。";
            _departmentUserApi = null;
            // 登录成功后重新读取会话，避免资源 API 继续使用控件初始化时的旧令牌或旧端口。
            _solidWorksResourceApi = null;
            // 管理员和部门人员页面只对管理员显示；普通登录用户不能进入管理操作界面。
            UIElement adminModuleTab = FindElement<UIElement>("SW_AdminModuleTab");
            UIElement departmentUsersModuleTab = FindElement<UIElement>("SW_DepartmentUsersModuleTab");
            if (adminModuleTab != null) adminModuleTab.Visibility = _solidWorksAdministrator ? Visibility.Visible : Visibility.Collapsed;
            if (departmentUsersModuleTab != null) departmentUsersModuleTab.Visibility = _solidWorksAdministrator ? Visibility.Visible : Visibility.Collapsed;
            if (authenticated)
            {
                SharedLoginSession session = new SharedLoginSessionStore().Load();
                _departmentUserApi = new UnifiedDepartmentUserApiService(session);
                _solidWorksCategoryApi = new SolidWorksCategoryApiService(session);
                _solidWorksResourceApi = new SolidWorksResourceApiService(session);
                _ = RefreshSolidWorksCategoryTreeAsync();
                _ = RefreshSolidWorksDepartmentsAsync();
            }
            else
            {
                SW_DepartmentsGrid.ItemsSource = null;
                SW_UsersGrid.ItemsSource = null;
                SW_CategoryTreeView.ItemsSource = null;
                // 退出登录时清空公用图元按钮，避免旧会话的业务内容继续留在界面上。
                PublicButtonsPanel.Children.Clear();
                SetPublicElementsStatus("登录后加载 SolidWorks 数据库图元。");
                _solidWorksCategoryApi = null;
                _solidWorksResourceApi = null;
                _selectedSolidWorksCategoryNode = null;
                _selectedSolidWorksElement = null;
                _selectedSolidWorksResource = null;
            }
        }

        private string GetAssemblyInstanceBaseName(SolidWorksResourceApiService.ResourceDto resource, string cachePath)
        {
            string originalFileName = Path.GetFileName(resource.OriginalFileName ?? string.Empty).Trim();
            string name = Path.GetFileNameWithoutExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileNameWithoutExtension(cachePath);
            if (string.IsNullOrWhiteSpace(name))
                name = resource.Id.ToString();
            return name.Trim();
        }

        private void ApplyAssemblyInstanceName(Component2 component, string baseName, AssemblyDoc assembly)
        {
            if (component == null || string.IsNullOrWhiteSpace(baseName)) return;
            int nextNumber = 1;
            object componentsObject = assembly.GetComponents(true);
            Component2[] components = componentsObject as Component2[];
            if (components != null)
            {
                foreach (Component2 existing in components)
                {
                    string existingName = existing?.Name2 ?? string.Empty;
                    if (!existingName.StartsWith(baseName + "(", StringComparison.OrdinalIgnoreCase)) continue;
                    int start = baseName.Length + 1;
                    int end = existingName.IndexOf(')', start);
                    int number;
                    if (end > start && int.TryParse(existingName.Substring(start, end - start), out number))
                        nextNumber = Math.Max(nextNumber, number + 1);
                }
            }
            string instanceName = baseName + "(" + nextNumber + ")";
            component.Name2 = instanceName;
            SolidWorksFileLogger.Info("已设置装配体零部件实例名称；名称=" + instanceName + "。" );
        }

        private async Task RefreshSolidWorksCategoryTreeAsync()
        {
            if (_solidWorksCategoryApi == null) return;
            try
            {
                SolidWorksCategoryApiService.CategoryTreeResponse response = await _solidWorksCategoryApi.GetTreeAsync();
                if (!response.Success)
                {
                    SW_CategoryTreeView.ItemsSource = null;
                    _solidWorksCategoryNodes = new List<SolidWorksCategoryNode>();
                    TxtStatus.Text = "分类树查询失败：" + response.Message;
                    Services.SolidWorksFileLogger.Warning("SolidWorks 分类树查询返回失败：" + response.Message);
                    return;
                }
                _solidWorksCategoryNodes = response.Categories.Select(category => new SolidWorksCategoryNode
                {
                    Id = category.Id,
                    Name = category.Name,
                    DisplayName = string.IsNullOrWhiteSpace(category.DisplayName) ? category.Name : category.DisplayName,
                    SortOrder = category.SortOrder,
                    Level = 0
                }).ToList();

                foreach (SolidWorksCategoryApiService.SubcategoryDto child in response.Subcategories ?? new List<SolidWorksCategoryApiService.SubcategoryDto>())
                {
                    SolidWorksCategoryNode parent = _solidWorksCategoryNodes.FirstOrDefault(node => node.Id == child.ParentId);
                    if (parent == null) continue;
                    parent.Children.Add(new SolidWorksCategoryNode
                    {
                        Id = child.Id,
                        ParentId = child.ParentId,
                        Name = child.Name,
                        DisplayName = string.IsNullOrWhiteSpace(child.DisplayName) ? child.Name : child.DisplayName,
                        SortOrder = child.SortOrder,
                        Level = 1
                    });
                }

                foreach (SolidWorksCategoryNode node in _solidWorksCategoryNodes)
                    node.Children = node.Children.OrderBy(child => child.SortOrder).ToList();
                _solidWorksCategoryNodes = _solidWorksCategoryNodes.OrderBy(node => node.SortOrder).ToList();
                SW_CategoryTreeView.ItemsSource = _solidWorksCategoryNodes;
                // 分类树加载完成后，同时刷新公用图元按钮；按钮和管理员模块共用同一套分类 ID。
                await RefreshSolidWorksPublicElementButtonsAsync();
                TxtStatus.Text = "已刷新分类树，共 " + _solidWorksCategoryNodes.Count + " 个主分类。";
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("SolidWorks 分类树刷新失败：" + ex.Message);
                TxtStatus.Text = "分类树刷新失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 按 CAD 主界面相同的“分类→图元→动态按钮”思路加载 SolidWorks 图元。
        /// SolidWorks 端只查询 platform=SOLIDWORKS 的统一构件，不读取或复制 CAD 的 DWG 文件。
        /// </summary>
        private async Task RefreshSolidWorksPublicElementButtonsAsync()
        {
            try
            {
                // 公用图元不再承载按主分类组织的数据库图元；先清理上一轮动态内容，保留各页面原有静态按钮。
                PublicButtonsPanel.Children.Clear();
                SetPublicElementsStatus("正在按数据库分类加载 SolidWorks 图元...");
                if (_solidWorksResourceApi == null)
                    _solidWorksResourceApi = new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());

                Dictionary<string, string> panelNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["共用图元"] = "PublicButtonsPanel",
                    ["工艺"] = "SolidWorksCraftDynamicPanel",
                    ["建筑"] = "SolidWorksArchitectureDynamicPanel",
                    ["结构"] = "SolidWorksStructureDynamicPanel",
                    ["给排水"] = "SolidWorksPlumbingDynamicPanel",
                    ["暖通"] = "SolidWorksHvacDynamicPanel",
                    ["电气"] = "SolidWorksElectricalDynamicPanel",
                    ["自控"] = "SolidWorksControlDynamicPanel",
                    ["总图"] = "SolidWorksGeneralDynamicPanel"
                };
                int elementCount = 0;
                foreach (SolidWorksCategoryNode mainCategory in _solidWorksCategoryNodes)
                {
                    string panelName = ResolveSolidWorksMainCategoryPanelName(mainCategory, panelNames);
                    Panel targetPanel = string.IsNullOrWhiteSpace(panelName) ? null : FindElement<Panel>(panelName);
                    if (targetPanel == null)
                    {
                        SolidWorksFileLogger.Warning("SolidWorks 主分类没有匹配的图元 TabItem；分类=" + mainCategory.DisplayText + "。" );
                        continue;
                    }

                    targetPanel.Children.Clear();
                    // 主分类本身也可能直接绑定图元，直接放到对应主分类 TabItem 的动态区域。
                    elementCount += await AddSolidWorksElementButtonsForCategoryAsync(mainCategory, targetPanel, null);
                    foreach (SolidWorksCategoryNode childCategory in mainCategory.Children)
                    {
                        // 每个子分类在对应主分类 TabItem 中独立创建 GroupBox，Header 使用数据库显示名称。
                        elementCount += await AddSolidWorksElementButtonsForCategoryAsync(childCategory, targetPanel, childCategory.DisplayText);
                    }
                }

                SetPublicElementsStatus(elementCount == 0
                    ? "数据库中暂无 SolidWorks 图元。"
                    : "已加载 SolidWorks 数据库图元 " + elementCount + " 个。点击按钮查看属性和预览。");
                SolidWorksFileLogger.Complete("加载 SolidWorks 公用图元按钮");
            }
            catch (Exception ex)
            {
                SetPublicElementsStatus("SolidWorks 图元加载失败：" + ex.Message);
                SolidWorksFileLogger.Failed("加载 SolidWorks 公用图元按钮", ex);
            }
        }

        /// <summary>按数据库主分类名称匹配八个固定的 SolidWorks 图元 TabItem。</summary>
        private static string ResolveSolidWorksMainCategoryPanelName(
            SolidWorksCategoryNode category,
            Dictionary<string, string> panelNames)
        {
            if (category == null) return null;
            string[] values = { category.Name, category.DisplayName };
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                string normalized = value.Trim();
                // 数据库名称前面的数字和连接符仅用于排序，匹配 TabItem 时必须去除，例如“01-工艺”转换为“工艺”。
                int separatorIndex = normalized.IndexOf('-');
                if (separatorIndex > 0 && normalized.Substring(0, separatorIndex).All(char.IsDigit))
                    normalized = normalized.Substring(separatorIndex + 1).Trim();
                string panelName;
                if (panelNames.TryGetValue(normalized, out panelName)) return panelName;
                if (normalized.EndsWith("图元", StringComparison.OrdinalIgnoreCase) &&
                    panelNames.TryGetValue(normalized.Substring(0, normalized.Length - 2), out panelName))
                    return panelName;
            }
            return null;
        }

        /// <summary>通过查找控件更新公用图元状态，兼容当前项目的 WPF XAML 生成方式。</summary>
        private void SetPublicElementsStatus(string message)
        {
            TextBlock statusText = FindElement<TextBlock>("PublicElementsStatusText");
            if (statusText != null) statusText.Text = message;
        }

        /// <summary>查询一个分类下的 SolidWorks 图元并创建动态按钮。</summary>
        private async Task<int> AddSolidWorksElementButtonsForCategoryAsync(
            SolidWorksCategoryNode category,
            Panel targetPanel,
            string subcategoryHeader)
        {
            if (category == null || category.Id <= 0 || targetPanel == null) return 0;

            SolidWorksResourceApiService.ElementListResponse response =
                await _solidWorksResourceApi.ListElementsByCategoryAsync(category.Id);
            if (!response.Success)
            {
                SolidWorksFileLogger.Warning("SolidWorks 分类图元查询失败；分类ID=" + category.Id + "；消息=" + response.Message);
                return 0;
            }

            List<SolidWorksResourceApiService.ElementDto> elements = response.Elements ?? new List<SolidWorksResourceApiService.ElementDto>();
            if (elements.Count == 0) return 0;

            Panel buttonPanel;
            if (string.IsNullOrWhiteSpace(subcategoryHeader))
            {
                buttonPanel = new WrapPanel { Margin = new Thickness(2, 2, 2, 4) };
                targetPanel.Children.Add(buttonPanel);
            }
            else
            {
                // Name 使用稳定的分类 ID，避免不同父分类下出现同名子分类时发生 WPF 名称冲突。
                string safeName = "SWSubcategory_" + category.Id + "_" + ToSolidWorksElementName(category.Name);
                var groupBox = new GroupBox
                {
                    Name = safeName,
                    Header = subcategoryHeader,
                    Margin = new Thickness(2, 2, 2, 4),
                    Padding = new Thickness(4, 2, 4, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                buttonPanel = new WrapPanel();
                groupBox.Content = buttonPanel;
                targetPanel.Children.Add(groupBox);
            }

            foreach (SolidWorksResourceApiService.ElementDto element in elements)
            {
                string displayName = string.IsNullOrWhiteSpace(element.DisplayName) ? element.Name : element.DisplayName;
                var button = new Button
                {
                    Content = displayName,
                    Tag = element,
                    Width = 118,
                    Height = 28,
                    Margin = new Thickness(1),
                    Padding = new Thickness(2, 0, 2, 0),
                    ToolTip = "构件编码：" + element.ElementCode,
                    Background = Brushes.Azure
                };
                button.Click += SolidWorksPublicElementButton_Click;
                button.MouseDoubleClick += SolidWorksPublicElementButton_MouseDoubleClick;
                button.PreviewMouseLeftButtonDown += SolidWorksPublicElementButton_PreviewMouseLeftButtonDown;
                button.PreviewMouseMove += SolidWorksPublicElementButton_PreviewMouseMove;
                buttonPanel.Children.Add(button);
            }

            return elements.Count;
        }

        private async void SolidWorksPublicElementButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (!(sender is Button button) || !(button.Tag is SolidWorksResourceApiService.ElementDto element))
            {
                SolidWorksFileLogger.Skipped("双击插入 SolidWorks 图元", "按钮没有绑定有效构件");
                return;
            }
            SolidWorksFileLogger.Start("双击插入 SolidWorks 图元；构件ID=" + element.Id);
            await InsertSolidWorksElementAsync(element, null);
        }

        private void SolidWorksPublicElementButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _solidWorksButtonDragStartPoint = e.GetPosition(null);
            SolidWorksFileLogger.Info("开始检测 SolidWorks 图元按钮拖动。" );
        }

        private void SolidWorksPublicElementButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || !(sender is Button button) || !(button.Tag is SolidWorksResourceApiService.ElementDto element)) return;
            Point currentPoint = e.GetPosition(null);
            if (Math.Abs(currentPoint.X - _solidWorksButtonDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _solidWorksButtonDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            DataObject data = new DataObject(typeof(SolidWorksResourceApiService.ElementDto), element);
            SolidWorksFileLogger.Info("开始执行 SolidWorks 图元按钮拖放；构件ID=" + element.Id + "。" );
            DragDrop.DoDragDrop(button, data, DragDropEffects.Copy);
        }

        /// <summary>统一执行 SolidWorks 图元插入：双击使用默认位置，拖放使用拖放位置。</summary>
        private async Task InsertSolidWorksElementAsync(SolidWorksResourceApiService.ElementDto element, Point? dropPoint)
        {
            if (element == null || SW == null)
            {
                SolidWorksFileLogger.Skipped("插入 SolidWorks 图元", element == null ? "构件数据为空" : "SolidWorks 应用程序对象未连接");
                TxtStatus.Text = "SolidWorks 尚未连接，无法插入图元。";
                return;
            }

            SolidWorksFileLogger.Start("准备插入 SolidWorks 图元；构件ID=" + element.Id + "；方式=" + (dropPoint.HasValue ? "拖放" : "双击"));
            bool previewModelOpened = false;
            string cachePath = null;
            try
            {
                EnsureSolidWorksResourceApi();
                SolidWorksFileLogger.Info("开始查询 SolidWorks 图元资源；构件ID=" + element.Id + "。" );
                SolidWorksResourceApiService.ResourceListResponse response = await _solidWorksResourceApi.ListAsync(element.Id);
                SolidWorksResourceApiService.ResourceDto resource = response.Resources.FirstOrDefault();
                if (resource == null)
                {
                    SolidWorksFileLogger.Skipped("插入 SolidWorks 图元", "当前构件没有模型资源；构件ID=" + element.Id);
                    TxtStatus.Text = "当前图元没有可插入的 SolidWorks 模型资源。";
                    return;
                }

                string extension = string.Equals(resource.ResourceType, "SLDASM", StringComparison.OrdinalIgnoreCase) ? ".sldasm" : ".sldprt";
                cachePath = _solidWorksLocalCache.TryGetModel(resource, extension);
                if (cachePath == null)
                {
                    SolidWorksFileLogger.Info("SolidWorks 模型缓存未命中，开始下载；资源ID=" + resource.Id + "。" );
                    byte[] modelBytes = await _solidWorksResourceApi.DownloadModelAsync(resource.Id);
                    cachePath = await _solidWorksLocalCache.WriteAsync(_solidWorksLocalCache.GetModelPath(resource, extension), modelBytes, "SolidWorks 模型");
                }

                ModelDoc2 activeDocument = SW.ActiveDoc as ModelDoc2;
                if (activeDocument == null)
                {
                    SolidWorksFileLogger.Skipped("插入 SolidWorks 图元", "当前没有打开的工程图或装配体文档");
                    TxtStatus.Text = "请先打开 SolidWorks 工程图或装配体文档。";
                    return;
                }

                int documentType = activeDocument.GetType();
                if (documentType == (int)swDocumentTypes_e.swDocDRAWING)
                {
                     SolidWorksFileLogger.Info("已识别当前文档为工程图，等待用户在图纸空间单击插入位置。" );
                    DrawingDoc drawing = activeDocument as DrawingDoc;
                    if (drawing == null) throw new InvalidOperationException("当前工程图文档接口不可用。" );
                    string viewName = string.IsNullOrWhiteSpace(resource.ConfigurationName) ? "*前视" : resource.ConfigurationName;
                     Point? drawingPoint = await WaitForDrawingInsertPointAsync(activeDocument);
                     if (!drawingPoint.HasValue)
                     {
                         TxtStatus.Text = "已取消 SolidWorks 工程图插入。";
                         return;
                     }
                     SolidWorksFileLogger.Info("已取得工程图插入位置；X=" + drawingPoint.Value.X.ToString("0.######") + "；Y=" + drawingPoint.Value.Y.ToString("0.######") + "。" );
                     View view = drawing.CreateDrawViewFromModelView3(cachePath, viewName, drawingPoint.Value.X, drawingPoint.Value.Y, 0.0);
                    if (view == null) throw new InvalidOperationException("工程图模型视图创建失败。请确认当前图纸存在有效图纸页。" );
                    TxtStatus.Text = "SolidWorks 图元已插入当前工程图。";
                }

                else if (documentType == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    SolidWorksFileLogger.Info("已识别当前文档为装配体，开始创建跟随鼠标的零部件预览。" );
                    AssemblyDoc assembly = activeDocument as AssemblyDoc;
                    if (assembly == null) throw new InvalidOperationException("当前装配体文档接口不可用。" );
                    SolidWorksFileLogger.Info("装配体状态检查；只读=" + (activeDocument.IsOpenedReadOnly() ? "是" : "否") + "；路径=" + activeDocument.GetPathName() + "。" );
                    if (activeDocument.IsOpenedReadOnly())
                        throw new InvalidOperationException("当前装配体以只读方式打开，不能插入零部件。请关闭后以可写方式重新打开。" );
                    // SolidWorks 原生插入流程会先打开模型文档并读取活动配置；这里恢复该流程，确保 SW 2022 能正确解析组件。
                    int sourceDocumentType = string.Equals(resource.ResourceType, "SLDASM", StringComparison.OrdinalIgnoreCase)
                        ? (int)swDocumentTypes_e.swDocASSEMBLY
                        : (int)swDocumentTypes_e.swDocPART;
                    int openErrors = 0;
                    int openWarnings = 0;
                    ModelDoc2 loadedModel = SW.OpenDoc6(cachePath, sourceDocumentType, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty, ref openErrors, ref openWarnings);
                    if (loadedModel == null)
                        throw new InvalidOperationException("SolidWorks 无法打开预览模型；错误码=" + openErrors + "；警告码=" + openWarnings + "。" );
                    previewModelOpened = true;
                    string configurationName = (resource.ConfigurationName ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(configurationName))
                        configurationName = loadedModel.ConfigurationManager.ActiveConfiguration.Name;
                    SolidWorksFileLogger.Info("预览模型已打开；配置=" + configurationName + "；错误码=" + openErrors + "；警告码=" + openWarnings + "。" );
                    Component2 component = assembly.AddComponent5(cachePath, 0, configurationName, false, string.Empty, 0.0, 0.0, 0.0);
                    SolidWorksFileLogger.Info("已调用 AddComponent5 插入装配体组件；是否取得组件=" + (component != null ? "是" : "否") + "。" );
                    if (component == null)
                    {
                        SolidWorksFileLogger.Warning("AddComponent5 未返回零部件；开始尝试旧兼容接口。" );
                        if (component == null)
                        {
                            bool inserted = assembly.AddComponent(cachePath, 0.0, 0.0, 0.0);
                            if (!inserted)
                                throw new InvalidOperationException("装配体零部件插入失败。请确认装配体可编辑、模型文件未损坏且配置名有效。" );
                            component = FindNewestAssemblyComponent(assembly, cachePath);
                            if (component == null)
                                throw new InvalidOperationException("装配体已返回插入成功，但无法取得零部件对象以执行鼠标跟随。" );
                            SolidWorksFileLogger.Info("已通过兼容插入接口添加并取得装配体零部件对象。" );
                        }
                    }
                    ApplyAssemblyInstanceName(component, GetAssemblyInstanceBaseName(resource, cachePath), assembly);
                    // 原生“插入零部件”创建的是可移动零部件；这里也必须确保 API 创建的组件不是固定状态。
                    EnsureAssemblyComponentIsFloating(activeDocument, assembly, component);
                    // 组件刚创建时 SolidWorks 默认放在原点；先刷新图形区，再订阅位置选择事件，避免预览状态停留在默认位置。
                    activeDocument.GraphicsRedraw2();
                    Point? assemblyPoint = await WaitForAssemblyComponentPlacementAsync(activeDocument, component);
                    if (!assemblyPoint.HasValue)
                    {
                        DeleteAssemblyPreviewComponent(activeDocument, component);
                        SW.CloseDoc(cachePath);
                        previewModelOpened = false;
                        TxtStatus.Text = "已取消 SolidWorks 装配体插入。";
                        return;
                    }
                    SolidWorksFileLogger.Info("已确认装配体零部件落点；预览模型窗口将在插入完成后关闭。" );
                    SW.CloseDoc(cachePath);
                    previewModelOpened = false;
                    TxtStatus.Text = "SolidWorks 图元已插入当前装配体。";
                }
                else
                {
                    SolidWorksFileLogger.Skipped("插入 SolidWorks 图元", "当前文档不是工程图或装配体");
                    TxtStatus.Text = "当前文档不是工程图或装配体，无法插入 SolidWorks 图元。";
                    return;
                }
                SolidWorksFileLogger.Complete("插入 SolidWorks 图元；构件ID=" + element.Id + "；资源ID=" + resource.Id);
            }
            catch (Exception ex)
            {
                if (previewModelOpened)
                {
                    try { SW.CloseDoc(cachePath); } catch { }
                }
                SolidWorksFileLogger.Failed("插入 SolidWorks 图元；构件ID=" + element.Id, ex);
                TxtStatus.Text = "插入 SolidWorks 图元失败：" + ex.Message;
            }
        }

        /// <summary>等待 SolidWorks 图形区选择工程图位置，鼠标选择回调返回工程图坐标（单位为米）。</summary>
        private Task<Point?> WaitForDrawingInsertPointAsync(ModelDoc2 drawingDocument)
        {
            CancelDrawingInsertPointSelection("重新开始工程图位置选择");
            SolidWorks.Interop.sldworks.Mouse mouse = drawingDocument.IActiveView?.GetMouse();
            if (mouse == null)
                throw new InvalidOperationException("无法获取 SolidWorks 工程图鼠标事件对象。" );

            _drawingMouseObject = mouse;
            _drawingInsertPointSource = new TaskCompletionSource<Point?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _drawingMouseDownHandler = (message, x, y) =>
            {
                if (_drawingInsertPointSource == null) return 0;
                ModelView modelView = drawingDocument.IActiveView;
                if (modelView == null) return 0;
                double modelX = 0.0;
                double modelY = 0.0;
                double modelZ = 0.0;
                modelView.UnprojectModelPoint(x, y, 0.0, out modelX, out modelY, out modelZ);
                SolidWorksFileLogger.Info("收到 SolidWorks 图形区左键位置；屏幕坐标=" + x + "," + y + "；模型坐标=" + modelX.ToString("0.######") + "," + modelY.ToString("0.######") + "," + modelZ.ToString("0.######") + "。" );
                _drawingInsertPointSource.TrySetResult(new Point(modelX, modelY));
                return 1;
            };
            _drawingMouseCancelHandler = (message, x, y) =>
            {
                if (_drawingInsertPointSource != null)
                {
                    SolidWorksFileLogger.Info("用户右键取消 SolidWorks 工程图位置选择。" );
                    _drawingInsertPointSource.TrySetResult(null);
                }
                return 1;
            };

            DMouseEvents_Event events = mouse as DMouseEvents_Event;
            if (events == null)
                throw new InvalidOperationException("当前 SolidWorks 版本不支持工程图鼠标事件。" );
            events.MouseLBtnDownNotify += _drawingMouseDownHandler;
            events.MouseRBtnDownNotify += _drawingMouseCancelHandler;
            TxtStatus.Text = "请在 SolidWorks 当前图形区中单击插入位置，右键取消。";
            SolidWorksFileLogger.Info("已订阅 SolidWorks 图形区左键位置选择事件。" );
            return WaitForDrawingInsertPointAndCleanupAsync();
        }

        /// <summary>创建装配体零部件后，使其跟随鼠标移动，左键确认、右键取消。</summary>
        private Task<Point?> WaitForAssemblyComponentPlacementAsync(ModelDoc2 assemblyDocument, Component2 component)
        {
            CancelDrawingInsertPointSelection("重新开始装配体零部件位置选择");
            ModelView modelView = assemblyDocument.IActiveView;
            if (modelView == null)
                throw new InvalidOperationException("无法获取 SolidWorks 装配体图形视图。" );

            // 资源库是 SolidWorks 右侧任务窗格，双击按钮后焦点仍可能停留在 WPF 控件；
            // 必须先激活模型视图，否则 MouseMoveNotify 不会接收到图形区鼠标消息。
            modelView.Activate();
            SolidWorks.Interop.sldworks.Mouse mouse = modelView.GetMouse();
            if (mouse == null)
                throw new InvalidOperationException("无法获取 SolidWorks 装配体鼠标事件对象。" );

            _assemblyPreviewComponent = component;
            _assemblyPreviewModel = assemblyDocument;
            _drawingMouseObject = mouse;
            _drawingInsertPointSource = new TaskCompletionSource<Point?>(TaskCreationOptions.RunContinuationsAsynchronously);
            DMouseEvents_Event events = mouse as DMouseEvents_Event;
            if (events == null)
                throw new InvalidOperationException("当前 SolidWorks 版本不支持装配体鼠标事件。" );

            bool mouseMoveEventObserved = false;
            _drawingMouseMoveHandler = (message, x, y) =>
            {
                if (_drawingInsertPointSource == null || _assemblyPreviewComponent == null) return 0;
                ModelPoint modelPoint = ConvertMouseToModelPoint(assemblyDocument, x, y);
                bool changed = TryUpdateAssemblyPreviewPosition(assemblyDocument, modelPoint);
                if (changed && !mouseMoveEventObserved)
                {
                    mouseMoveEventObserved = true;
                    SolidWorksFileLogger.Info("已收到 SolidWorks 图形区鼠标移动事件并更新装配体预览。" );
                }
                return 1;
            };
            _drawingMouseDownHandler = (message, x, y) =>
            {
                if (_drawingInsertPointSource == null) return 0;
                ModelPoint modelPoint = ConvertMouseToModelPoint(assemblyDocument, x, y);
                _drawingInsertPointSource.TrySetResult(new Point(modelPoint.X, modelPoint.Y));
                return 1;
            };
            _drawingMouseCancelHandler = (message, x, y) =>
            {
                if (_drawingInsertPointSource != null)
                    _drawingInsertPointSource.TrySetResult(null);
                return 1;
            };
            events.MouseMoveNotify += _drawingMouseMoveHandler;
            events.MouseLBtnDownNotify += _drawingMouseDownHandler;
            events.MouseRBtnDownNotify += _drawingMouseCancelHandler;
            StartAssemblyPlacementCursorFallback(assemblyDocument);
            TxtStatus.Text = "模型正在跟随鼠标移动，单击确定插入位置，右键取消。";
            SolidWorksFileLogger.Info("已创建装配体零部件预览并订阅鼠标移动事件。" );
            return WaitForDrawingInsertPointAndCleanupAsync();
        }

        /// <summary>使用系统光标轮询作为备用通道，避免右侧任务窗格焦点导致 SolidWorks 鼠标事件不回调。</summary>
        private void StartAssemblyPlacementCursorFallback(ModelDoc2 assemblyDocument)
        {
            StopAssemblyPlacementCursorFallback();
            _assemblyPlacementLastLeftButtonDown = IsMouseButtonDown(0x01);
            _assemblyPlacementLastRightButtonDown = IsMouseButtonDown(0x02);
            _assemblyPlacementTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _assemblyPlacementTimer.Tick += (sender, args) =>
            {
                if (_drawingInsertPointSource == null || _assemblyPreviewComponent == null) return;

                ModelPoint modelPoint;
                bool cursorInView = TryGetCursorModelPoint(assemblyDocument, out modelPoint);
                if (cursorInView)
                    TryUpdateAssemblyPreviewPosition(assemblyDocument, modelPoint);

                bool leftButtonDown = IsMouseButtonDown(0x01);
                bool rightButtonDown = IsMouseButtonDown(0x02);
                if (rightButtonDown && !_assemblyPlacementLastRightButtonDown)
                    _drawingInsertPointSource.TrySetResult(null);
                else if (leftButtonDown && !_assemblyPlacementLastLeftButtonDown && cursorInView)
                    _drawingInsertPointSource.TrySetResult(new Point(modelPoint.X, modelPoint.Y));

                _assemblyPlacementLastLeftButtonDown = leftButtonDown;
                _assemblyPlacementLastRightButtonDown = rightButtonDown;
            };
            _assemblyPlacementTimer.Start();
            SolidWorksFileLogger.Info("已启动装配体鼠标位置备用检测。" );
        }

        private bool TryUpdateAssemblyPreviewPosition(ModelDoc2 assemblyDocument, ModelPoint modelPoint)
        {
            SolidWorks.Interop.sldworks.MathUtility mathUtility = SW.IGetMathUtility();
            if (mathUtility == null) return false;

            ModelView modelView = assemblyDocument.IActiveView;
            if (modelView == null) return false;

            double[] transformData = new double[16];
            // SolidWorks MathTransform 数组前 9 个值是旋转矩阵，9~11 是平移坐标，12 是缩放比例。
            // 使用活动视图的逆方向矩阵，使模型正面按 SolidWorks 原生插入规则朝向当前视图。
            MathTransform viewOrientation = modelView.Orientation3;
            MathTransform placementOrientation = viewOrientation == null ? null : viewOrientation.IInverse();
            double[] orientationData = GetMathTransformArray(placementOrientation);
            if (orientationData != null && orientationData.Length >= 9)
            {
                for (int index = 0; index < 9; index++)
                    transformData[index] = orientationData[index];
            }
            else
            {
                transformData[0] = 1.0;
                transformData[4] = 1.0;
                transformData[8] = 1.0;
            }

            transformData[9] = modelPoint.X;
            transformData[10] = modelPoint.Y;
            transformData[11] = modelPoint.Z;
            transformData[12] = 1.0;
            SolidWorks.Interop.sldworks.MathTransform transform = mathUtility.ICreateTransform(ref transformData[0]);
            if (transform == null) return false;
            bool changed = _assemblyPreviewComponent.SetTransformAndSolve2(transform);
            assemblyDocument.GraphicsRedraw2();
            return changed;
        }

        private bool TryGetCursorModelPoint(ModelDoc2 document, out ModelPoint modelPoint)
        {
            modelPoint = new ModelPoint();
            ModelView modelView = document.IActiveView;
            if (modelView == null) return false;
            IntPtr viewHandle = new IntPtr(modelView.GetViewHWndx64());
            if (viewHandle == IntPtr.Zero) return false;
            NativePoint screenPoint;
            if (!GetCursorPos(out screenPoint)) return false;
            if (!ScreenToClient(viewHandle, ref screenPoint)) return false;
            NativeRect clientRect;
            if (!GetClientRect(viewHandle, out clientRect)) return false;
            if (screenPoint.X < clientRect.Left || screenPoint.Y < clientRect.Top ||
                screenPoint.X >= clientRect.Right || screenPoint.Y >= clientRect.Bottom) return false;
            double modelX;
            double modelY;
            double modelZ;
            modelView.UnprojectModelPoint(screenPoint.X, screenPoint.Y, 0.0, out modelX, out modelY, out modelZ);
            modelPoint = new ModelPoint(modelX, modelY, modelZ);
            return true;
        }

        private static double[] GetMathTransformArray(MathTransform transform)
        {
            if (transform == null) return null;
            Array values = transform.ArrayData as Array;
            if (values == null || values.Length < 9) return null;

            double[] result = new double[values.Length];
            for (int index = 0; index < values.Length; index++)
                result[index] = Convert.ToDouble(values.GetValue(index));
            return result;
        }

        private static bool IsMouseButtonDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private void StopAssemblyPlacementCursorFallback()
        {
            if (_assemblyPlacementTimer == null) return;
            _assemblyPlacementTimer.Stop();
            _assemblyPlacementTimer = null;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr windowHandle, ref NativePoint point);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr windowHandle, out NativeRect rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private struct ModelPoint
        {
            public ModelPoint(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X;
            public double Y;
            public double Z;
        }

        /// <summary>确保通过 API 创建的装配体零部件与 SolidWorks 原生插入零部件一样保持浮动。</summary>
        private void EnsureAssemblyComponentIsFloating(ModelDoc2 assemblyDocument, AssemblyDoc assembly, Component2 component)
        {
            if (component == null || !component.IsFixed()) return;

            component.Select(false);
            assembly.UnfixComponent();
            assemblyDocument.ClearSelection2(true);
            SolidWorksFileLogger.Info("已解除装配体零部件固定状态，允许其跟随鼠标移动。" );
        }

        private Component2 FindNewestAssemblyComponent(AssemblyDoc assembly, string modelPath)
        {
            Component2 newest = null;
            object componentsObject = assembly.GetComponents(true);
            Component2[] components = componentsObject as Component2[];
            if (components == null) return null;
            foreach (Component2 item in components)
            {
                if (item == null || !string.Equals(item.GetPathName(), modelPath, StringComparison.OrdinalIgnoreCase)) continue;
                newest = item;
            }
            return newest;
        }

        private ModelPoint ConvertMouseToModelPoint(ModelDoc2 document, int x, int y)
        {
            ModelView modelView = document.IActiveView;
            double modelX;
            double modelY;
            double modelZ;
            modelView.UnprojectModelPoint(x, y, 0.0, out modelX, out modelY, out modelZ);
            return new ModelPoint(modelX, modelY, modelZ);
        }

        private void DeleteAssemblyPreviewComponent(ModelDoc2 document, Component2 component)
        {
            if (component == null) return;
            try
            {
                component.Select(false);
                document.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                SolidWorksFileLogger.Info("已删除取消插入的装配体预览零部件。" );
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Warning("删除装配体预览零部件失败：" + ex.Message);
            }
        }

        private async Task<Point?> WaitForDrawingInsertPointAndCleanupAsync()
        {
            try
            {
                return await _drawingInsertPointSource.Task.ConfigureAwait(true);
            }
            finally
            {
                CancelDrawingInsertPointSelection("工程图位置选择已结束");
            }
        }

        /// <summary>取消并清理 SolidWorks 工程图鼠标事件，避免 COM 事件重复订阅。</summary>
        internal void CancelDrawingInsertPointSelection(string reason)
        {
            StopAssemblyPlacementCursorFallback();
            DMouseEvents_Event events = _drawingMouseObject as DMouseEvents_Event;
            if (events != null)
            {
                if (_drawingMouseMoveHandler != null) events.MouseMoveNotify -= _drawingMouseMoveHandler;
                if (_drawingMouseDownHandler != null) events.MouseLBtnDownNotify -= _drawingMouseDownHandler;
                if (_drawingMouseCancelHandler != null) events.MouseRBtnDownNotify -= _drawingMouseCancelHandler;
            }
            if (_drawingInsertPointSource != null && !_drawingInsertPointSource.Task.IsCompleted)
                _drawingInsertPointSource.TrySetResult(null);
            _drawingMouseObject = null;
            _drawingMouseSelectHandler = null;
            _drawingMouseMoveHandler = null;
            _drawingMouseDownHandler = null;
            _drawingMouseCancelHandler = null;
            _drawingInsertPointSource = null;
            _assemblyPreviewComponent = null;
            _assemblyPreviewModel = null;
            SolidWorksFileLogger.Info("清理 SolidWorks 工程图位置选择；原因=" + reason + "。" );
        }

        /// <summary>将数据库子分类名称转换为可用于 WPF FrameworkElement.Name 的稳定名称片段。</summary>
        private static string ToSolidWorksElementName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Category";
            char[] chars = value.Trim().Select(character =>
                char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray();
            string result = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "Category" : result;
        }

        /// <summary>公用图元按钮点击处理：选择构件并读取资源、属性和预览。</summary>
        private async void SolidWorksPublicElementButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is SolidWorksResourceApiService.ElementDto element)) return;

            SolidWorksFileLogger.Start("选择 SolidWorks 公用图元；构件ID=" + element.Id);
            _selectedSolidWorksElement = element;
            _selectedSolidWorksResource = null;
            ClearSolidWorksPreview("正在加载所选 SolidWorks 图元的预览...");
            TxtStatus.Text = "正在加载 SolidWorks 图元：" + (string.IsNullOrWhiteSpace(element.DisplayName) ? element.Name : element.DisplayName) + "。";

            try
            {
                EnsureSolidWorksResourceApi();
                SolidWorksResourceApiService.ResourceListResponse response = await _solidWorksResourceApi.ListAsync(element.Id);
                if (!response.Success)
                {
                    TxtStatus.Text = "图元资源查询失败：" + response.Message;
                    return;
                }

                SolidWorksResourceApiService.ResourceDto resource = response.Resources.FirstOrDefault();
                if (resource == null)
                {
                    ClearSolidWorksPreview("当前图元没有可显示的 SolidWorks 资源。");
                    TxtStatus.Text = "图元已加载，但当前没有 SolidWorks 模型资源。";
                    return;
                }

                _selectedSolidWorksResource = resource;
                SW_FileName.Text = resource.ResourceName ?? string.Empty;
                SW_ClientVersion.Text = resource.ConfigurationName ?? string.Empty;
                SW_FileSize.Text = resource.ResourceType ?? string.Empty;
                await RefreshSolidWorksPropertiesAsync(element.Id);
                // 点击按钮可能早于构造函数中的异步 WebView2 初始化，这里再次等待初始化完成，避免 3D 预览请求过早失败。
                await InitializeThreeDPreviewAsync();
                 await PreloadSolidWorksResourceFilesAsync(resource);
                TxtStatus.Text = "已加载 SolidWorks 图元资源和预览；资源数=" + response.Resources.Count + "。";
                SolidWorksFileLogger.Complete("选择 SolidWorks 公用图元；构件ID=" + element.Id);
            }
             catch (Exception ex)
             {
                 SolidWorksFileLogger.Failed("选择 SolidWorks 公用图元；构件ID=" + element.Id, ex);
                 TxtStatus.Text = "加载 SolidWorks 图元失败：" + ex.Message;
             }
         }

        /// <summary>首次选择资源时预取模型、预览图片和三维附件，后续操作直接命中本地缓存。</summary>
        private async Task PreloadSolidWorksResourceFilesAsync(SolidWorksResourceApiService.ResourceDto resource)
        {
            string extension = string.Equals(resource.ResourceType, "SLDASM", StringComparison.OrdinalIgnoreCase) ? ".sldasm" : ".sldprt";
            if (_solidWorksLocalCache.TryGetModel(resource, extension) == null)
            {
                byte[] modelBytes = await _solidWorksResourceApi.DownloadModelAsync(resource.Id);
                await _solidWorksLocalCache.WriteAsync(_solidWorksLocalCache.GetModelPath(resource, extension), modelBytes, "SolidWorks 模型");
            }

            SolidWorksResourceApiService.AttachmentListResponse attachments = await _solidWorksResourceApi.ListAttachmentsAsync(resource.Id);
            foreach (SolidWorksResourceApiService.AttachmentDto attachment in attachments.Attachments ?? new List<SolidWorksResourceApiService.AttachmentDto>())
            {
                if (!string.Equals(attachment.AttachmentType, "PREVIEW_IMAGE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(attachment.AttachmentType, "PREVIEW_3D", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_solidWorksLocalCache.TryGetAttachment(resource.Id, attachment) != null) continue;
                byte[] bytes = await _solidWorksResourceApi.DownloadAttachmentAsync(attachment.Id);
                await _solidWorksLocalCache.WriteAsync(_solidWorksLocalCache.GetAttachmentPath(resource.Id, attachment), bytes, attachment.AttachmentType);
            }

            await LoadSolidWorksPreviewImageAsync(resource.Id, false);
            await LoadSolidWorksPreview3DAsync(resource.Id, false);
        }

        /// <summary>
        /// 清理上一个图元的预览内容，避免新图元没有附件时继续显示旧图元。
        /// </summary>
        private void ClearSolidWorksPreview(string message)
        {
            SW_ViewImage.Source = null;
            SW_ViewImage.Visibility = Visibility.Visible;
            if (SW_ThreeDPreviewHost.CoreWebView2 != null)
            {
                SW_ThreeDPreviewHost.NavigateToString(
                    "<html><body style='background:#111;color:#ddd;font-family:Microsoft YaHei'>"
                    + message
                    + "</body></html>");
            }
            SolidWorksFileLogger.Info("清理 SolidWorks 图元预览；原因=" + message);
        }

        private void SW_CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selectedSolidWorksCategoryNode = e.NewValue as SolidWorksCategoryNode;
            Services.SolidWorksFileLogger.Info("点击/选择 SolidWorks 分类；分类ID=" + (_selectedSolidWorksCategoryNode?.Id.ToString() ?? "无") + "。" );
            _ = RefreshSolidWorksElementsByCategoryAsync();
        }

        /// <summary>分类树选中后，按分类 ID 加载统一构件；后续模型操作必须绑定到具体构件 ID。</summary>
        private async Task RefreshSolidWorksElementsByCategoryAsync()
        {
            if (_selectedSolidWorksCategoryNode == null) return;
            if (!_solidWorksAdministrator)
            {
                TxtStatus.Text = "当前登录账号没有 SolidWorks 管理员权限，未查询统一构件。";
                return;
            }
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.ElementListResponse response = await _solidWorksResourceApi.ListElementsByCategoryAsync(_selectedSolidWorksCategoryNode.Id);
                if (!response.Success)
                {
                    _solidWorksElementsByCategory = new List<SolidWorksResourceApiService.ElementDto>();
                    SW_ElementSelectionComboBox.ItemsSource = null;
                    TxtStatus.Text = "统一构件查询失败：" + response.Message;
                    return;
                }
                _solidWorksElementsByCategory = response.Elements ?? new List<SolidWorksResourceApiService.ElementDto>();
                SW_ElementSelectionComboBox.SelectedItem = null;
                SW_ElementSelectionComboBox.ItemsSource = _solidWorksElementsByCategory
                    .Select(element => new SolidWorksElementSelectionItem
                    {
                        Id = element.Id,
                        DisplayText = string.IsNullOrWhiteSpace(element.DisplayName) ? element.Name : element.DisplayName
                    })
                    .ToList();
                TxtStatus.Text = _solidWorksElementsByCategory.Count == 0
                    ? "分类 ID=" + _selectedSolidWorksCategoryNode.Id + " 查询成功，但没有绑定统一构件。请检查 ELEMENT_DEFINITIONS.CATEGORY_ID。"
                    : "已选择分类 ID=" + _selectedSolidWorksCategoryNode.Id + "，构件数=" + _solidWorksElementsByCategory.Count + "。请在‘统一构件’下拉框选择具体构件。";
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("按分类加载 SolidWorks 构件失败：" + ex.Message);
                TxtStatus.Text = "按分类加载构件失败：" + ex.Message;
            }
        }

        private void SW_ElementSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(SW_ElementSelectionComboBox.SelectedItem is SolidWorksElementSelectionItem selected)) return;
            Services.SolidWorksFileLogger.Info("选择 SolidWorks 统一构件；构件ID=" + selected.Id + "。" );
            _selectedSolidWorksElement = _solidWorksElementsByCategory.FirstOrDefault(element => element.Id == selected.Id);
            _selectedSolidWorksResource = null;
            SW_StroageFileDataGrid.ItemsSource = null;
            TxtStatus.Text = _selectedSolidWorksElement == null
                ? "未找到所选统一构件。"
                : "已选择统一构件 ID=" + _selectedSolidWorksElement.Id + "。现在可以上传或更新 SolidWorks 图元。";
            if (_selectedSolidWorksElement != null)
            {
                _ = LoadAdminElementDataAsync(_selectedSolidWorksElement.Id);
            }
        }

        /// <summary>管理员选择统一构件后按固定顺序加载资源列表和共用属性，避免异步请求互相覆盖界面。</summary>
        private async Task LoadAdminElementDataAsync(long elementId)
        {
            await RefreshAdminSolidWorksResourcesAsync(elementId);
            await RefreshSolidWorksPropertiesAsync(elementId);
        }

        /// <summary>加载当前统一构件的共用属性，供 CAD 与 SolidWorks 共同使用。</summary>
        private async Task RefreshSolidWorksPropertiesAsync(long elementId)
        {
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.PropertyListResponse response = await _solidWorksResourceApi.ListPropertiesAsync(elementId);
                if (!response.Success)
                {
                    TxtStatus.Text = "构件属性查询失败：" + response.Message;
                    return;
                }
                SW_CategoryPropertiesDataGrid.ItemsSource = response.Data ?? new List<SolidWorksResourceApiService.PropertyValueDto>();
                TxtStatus.Text = "已加载统一构件属性，共 " + (response.Data?.Count ?? 0) + " 项。";
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("加载 SolidWorks 构件属性失败：" + ex.Message);
                TxtStatus.Text = "加载构件属性失败：" + ex.Message;
            }
        }

        private async Task RefreshAdminSolidWorksResourcesAsync(long? elementId = null)
        {
            long selectedElementId = elementId ?? _selectedSolidWorksElement?.Id ?? 0;
            if (selectedElementId <= 0 || !_solidWorksAdministrator) return;
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.ResourceListResponse response = await _solidWorksResourceApi.ListAsync(selectedElementId);
                if (!response.Success)
                {
                    TxtStatus.Text = "资源查询失败：" + response.Message;
                    return;
                }
                SW_StroageFileDataGrid.ItemsSource = response.Resources;
                TxtStatus.Text = response.Message + " SolidWorks资源数=" + response.Resources.Count + "。";
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("刷新 SolidWorks 资源失败：" + ex.Message);
                TxtStatus.Text = "刷新 SolidWorks 资源失败：" + ex.Message;
            }
        }

        private async void SW_AdminRefreshResources_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击 SolidWorks 管理员模块【刷新资源】按钮。" );
            await RefreshAdminSolidWorksResourcesAsync();
        }

        private void SW_StroageFileDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedSolidWorksResource = SW_StroageFileDataGrid.SelectedItem as SolidWorksResourceApiService.ResourceDto;
            if (_selectedSolidWorksResource != null)
            {
                SW_FileName.Text = _selectedSolidWorksResource.ResourceName ?? string.Empty;
                SW_ClientVersion.Text = _selectedSolidWorksResource.ConfigurationName ?? string.Empty;
                SW_FileSize.Text = _selectedSolidWorksResource.ResourceType ?? string.Empty;
                TxtStatus.Text = "已选择 SolidWorks 资源 ID=" + _selectedSolidWorksResource.Id + "。";
                _ = LoadAdminSolidWorksPreviewAsync(_selectedSolidWorksResource);
            }
        }

        /// <summary>异步加载管理员选中资源的属性、图片和三维预览，保持 WPF 事件签名兼容调试热重载。</summary>
        private async Task LoadAdminSolidWorksPreviewAsync(SolidWorksResourceApiService.ResourceDto resource)
        {
            await RefreshSolidWorksPropertiesAsync(resource.ElementDefinitionId);
            await InitializeThreeDPreviewAsync(true);
            await Task.WhenAll(
                LoadSolidWorksPreviewImageAsync(resource.Id, true),
                LoadSolidWorksPreview3DAsync(resource.Id, true));
        }

        /// <summary>右键资源行时先同步选中项，使右键菜单操作作用于当前行。</summary>
        private void SW_StroageFileDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.Item is SolidWorksResourceApiService.ResourceDto resource)
            {
                row.IsSelected = true;
                row.Focus();
                _selectedSolidWorksResource = resource;
                Services.SolidWorksFileLogger.Info("右键选择 SolidWorks 资源；资源ID=" + resource.Id + "。" );
            }
        }

        private async void SW_AdminPreview3D_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSolidWorksResource == null)
            {
                TxtStatus.Text = "请先在资源列表中选择 SolidWorks 资源。";
                return;
            }
            await LoadSolidWorksPreview3DAsync(_selectedSolidWorksResource.Id, true);
        }

        private async void SW_AdminPreviewImage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSolidWorksResource == null)
            {
                TxtStatus.Text = "请先在资源列表中选择 SolidWorks 资源。";
                return;
            }
            await LoadSolidWorksPreviewImageAsync(_selectedSolidWorksResource.Id, true);
        }

        /// <summary>下载当前资源的 GLB/GLTF 附件并加载到 WebView2 三维查看器。</summary>
        private async Task LoadSolidWorksPreview3DAsync(long resourceId, bool administratorPreview = false)
        {
            try
            {
                EnsureSolidWorksResourceApi();
                SolidWorksResourceApiService.AttachmentListResponse attachments = await _solidWorksResourceApi.ListAttachmentsAsync(resourceId);
                SolidWorksResourceApiService.AttachmentDto model = attachments.Attachments.FirstOrDefault(item => string.Equals(item.AttachmentType, "PREVIEW_3D", StringComparison.OrdinalIgnoreCase));
                if (model == null)
                {
                    TxtStatus.Text = "当前资源没有 3D 预览附件。";
                    return;
                }
                string filePath = _solidWorksLocalCache.TryGetAttachment(resourceId, model);
                if (filePath == null)
                {
                    SolidWorksFileLogger.Info("SolidWorks 三维预览缓存未命中，开始下载；附件ID=" + model.Id + "。" );
                    byte[] bytes = await _solidWorksResourceApi.DownloadAttachmentAsync(model.Id);
                    filePath = await _solidWorksLocalCache.WriteAsync(_solidWorksLocalCache.GetAttachmentPath(resourceId, model), bytes, "SolidWorks 三维预览");
                }
                LoadThreeDPreviewFile(filePath, administratorPreview);
                TxtStatus.Text = "已加载 SolidWorks 三维预览。";
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Warning("加载 SolidWorks 三维预览失败：" + ex.Message);
                TxtStatus.Text = "加载三维预览失败：" + ex.Message;
            }
        }

        /// <summary>将本地 GLB/GLTF 文件加载到交互式 WebView2 三维查看器。</summary>
        private void LoadThreeDPreviewFile(string modelFilePath, bool administratorPreview = false)
        {
            if (!File.Exists(modelFilePath))
                throw new FileNotFoundException("三维预览文件不存在。", modelFilePath);

            Microsoft.Web.WebView2.Wpf.WebView2 previewHost = administratorPreview
                ? SW_资源ThreeDPreviewHost
                : SW_ThreeDPreviewHost;
            if (previewHost.CoreWebView2 == null)
                throw new InvalidOperationException("WebView2 尚未初始化完成，请稍后再试。" );

            // SolidWorks 插件运行时的当前目录通常是 SolidWorks 主程序目录，不能使用 AppDomain.BaseDirectory 定位插件页面。
            string assemblyDirectory = Path.GetDirectoryName(typeof(RightResourceLibrary).Assembly.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            string pageDirectory = Path.Combine(assemblyDirectory, "DisplayPages");
            string pagePath = Path.Combine(pageDirectory, "ThreeDPreview.html");
            if (!File.Exists(pagePath))
                throw new FileNotFoundException("SolidWorks 三维预览页面不存在。请确认 ThreeDPreview.html 已复制到插件的 DisplayPages 目录。", pagePath);

            string modelDirectory = Path.GetDirectoryName(modelFilePath);
            if (string.IsNullOrWhiteSpace(modelDirectory))
                throw new InvalidOperationException("无法确定三维预览文件所在目录。" );

            string hostSuffix = administratorPreview ? "admin" : "public";
            string pageHost = "gb-cad-preview-" + hostSuffix + ".local";
            string modelHost = "gb-cad-model-" + hostSuffix + ".local";
            previewHost.CoreWebView2.SetVirtualHostNameToFolderMapping(
                pageHost,
                pageDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            previewHost.CoreWebView2.SetVirtualHostNameToFolderMapping(
                modelHost,
                modelDirectory,
                CoreWebView2HostResourceAccessKind.Allow);

            string modelUrl = "https://" + modelHost + "/" + Uri.EscapeDataString(Path.GetFileName(modelFilePath));
            string pageUrl = "https://" + pageHost + "/ThreeDPreview.html?model=" + Uri.EscapeDataString(modelUrl);
            Services.SolidWorksFileLogger.Info("加载 SolidWorks 三维预览页面；页面和模型使用 WebView2 虚拟主机路径。" );
            previewHost.Source = new Uri(pageUrl, UriKind.Absolute);
        }

        /// <summary>加载当前资源的预览图片到 SW_ViewImage；3D 附件不强行转换为图片。</summary>
        private async Task LoadSolidWorksPreviewImageAsync(long resourceId, bool administratorPreview = false)
        {
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.AttachmentListResponse attachments = await _solidWorksResourceApi.ListAttachmentsAsync(resourceId);
                SolidWorksResourceApiService.AttachmentDto image = attachments.Attachments.FirstOrDefault(item => string.Equals(item.AttachmentType, "PREVIEW_IMAGE", StringComparison.OrdinalIgnoreCase));
                if (image == null)
                {
                    System.Windows.Controls.Image previewImage = administratorPreview ? SW_资源ViewImage : SW_ViewImage;
                    previewImage.Source = null;
                    previewImage.Visibility = Visibility.Visible;
                    TxtStatus.Text = "当前资源没有预览图片；三维预览仍可在右侧查看。";
                    return;
                }
                string cachedImagePath = _solidWorksLocalCache.TryGetAttachment(resourceId, image);
                byte[] bytes;
                if (cachedImagePath == null)
                {
                    SolidWorksFileLogger.Info("SolidWorks 预览图片缓存未命中，开始下载；附件ID=" + image.Id + "。" );
                    bytes = await _solidWorksResourceApi.DownloadAttachmentAsync(image.Id);
                    cachedImagePath = await _solidWorksLocalCache.WriteAsync(_solidWorksLocalCache.GetAttachmentPath(resourceId, image), bytes, "SolidWorks 预览图片");
                }
                else
                {
                    bytes = File.ReadAllBytes(cachedImagePath);
                }
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    (administratorPreview ? SW_资源ViewImage : SW_ViewImage).Source = bitmap;
                }
                (administratorPreview ? SW_资源ViewImage : SW_ViewImage).Visibility = Visibility.Visible;
                TxtStatus.Text = "已加载 SolidWorks 图元预览图片；三维模型可在右侧同时查看。";
            }
            catch (Exception ex)
            {
                    System.Windows.Controls.Image previewImage = administratorPreview ? SW_资源ViewImage : SW_ViewImage;
                    previewImage.Source = null;
                    previewImage.Visibility = Visibility.Visible;
                Services.SolidWorksFileLogger.Warning("加载 SolidWorks 预览图片失败：" + ex.Message);
                TxtStatus.Text = "加载预览图片失败：" + ex.Message;
            }
        }

        private async void SW_AdminUpdateModel_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击 SolidWorks 管理员模块【更新模型】按钮；是否替换现有资源=" + (_selectedSolidWorksResource != null) + "。" );
            if (_selectedSolidWorksElement == null || string.IsNullOrWhiteSpace(SW_filePath.Text))
            {
                TxtStatus.Text = "请先选择统一构件和 SolidWorks 模型文件。";
                return;
            }
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                if (_selectedSolidWorksResource != null)
                {
                    await _solidWorksResourceApi.ReplaceModelAsync(
                        _selectedSolidWorksResource.Id,
                        SW_filePath.Text.Trim(),
                        string.IsNullOrWhiteSpace(SW_FileName.Text) ? _selectedSolidWorksResource.ResourceName : SW_FileName.Text.Trim(),
                        string.IsNullOrWhiteSpace(SW_ClientVersion.Text) ? _selectedSolidWorksResource.ConfigurationName : SW_ClientVersion.Text.Trim());
                }
                else
                {
                    await _solidWorksResourceApi.UploadModelAsync(_selectedSolidWorksElement.Id, SW_filePath.Text.Trim(), SW_FileName.Text.Trim(), SW_ClientVersion.Text.Trim());
                }
                TxtStatus.Text = "SolidWorks 模型更新成功。";
                await RefreshAdminSolidWorksResourcesAsync();
            }
            catch (Exception ex) { TxtStatus.Text = "更新 SolidWorks 模型失败：" + ex.Message; }
        }

        private async void SW_AdminReplacePreviewImage_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击 SolidWorks 管理员模块【替换预览图片】按钮。" );
            await UploadAdminPreviewAsync("PREVIEW_IMAGE", "预览图片|*.png;*.jpg;*.jpeg");
        }

        private async void SW_AdminReplacePreview3D_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击 SolidWorks 管理员模块【替换三维预览】按钮。" );
            await UploadAdminPreviewAsync("PREVIEW_3D", "轻量三维预览|*.glb;*.gltf");
        }

        private async Task UploadAdminPreviewAsync(string attachmentType, string filter)
        {
            if (_selectedSolidWorksResource == null)
            {
                TxtStatus.Text = "请先在资源列表中选择 SolidWorks 资源。";
                return;
            }
            OpenFileDialog dialog = new OpenFileDialog { Filter = filter };
            if (dialog.ShowDialog() != true) return;
            try
            {
                // 选择本地 GLB/GLTF 后先立即预览，上传成功后再从服务器附件重新加载验证。
                if (attachmentType == "PREVIEW_3D")
                {
                    LoadThreeDPreviewFile(dialog.FileName);
                    TxtStatus.Text = "已加载本地 3D 预览，正在上传附件。";
                }

                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.AttachmentResponse response = await _solidWorksResourceApi.UploadPreviewAsync(_selectedSolidWorksResource.Id, dialog.FileName, attachmentType);
                if (!response.Success)
                {
                    TxtStatus.Text = "预览附件上传失败：" + response.Message;
                    return;
                }

                TxtStatus.Text = attachmentType == "PREVIEW_3D"
                    ? "SolidWorks 3D 预览上传成功，正在刷新三维窗口。"
                    : "SolidWorks 预览图片上传成功，正在刷新图片窗口。";

                if (attachmentType == "PREVIEW_3D")
                    await LoadSolidWorksPreview3DAsync(_selectedSolidWorksResource.Id);
                else
                    await LoadSolidWorksPreviewImageAsync(_selectedSolidWorksResource.Id);
            }
            catch (Exception ex) { TxtStatus.Text = "替换 SolidWorks 预览附件失败：" + ex.Message; }
        }

        private async void SW_AdminArchiveResource_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击 SolidWorks 管理员模块【归档资源】按钮。" );
            if (_selectedSolidWorksResource == null)
            {
                TxtStatus.Text = "请先在资源列表中选择 SolidWorks 资源。";
                return;
            }
            if (MessageBox.Show("确认归档当前 SolidWorks 资源？", "SolidWorks 资源管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                await _solidWorksResourceApi.ArchiveResourceAsync(_selectedSolidWorksResource.Id);
                TxtStatus.Text = "SolidWorks 资源已归档。";
                await RefreshAdminSolidWorksResourcesAsync();
            }
            catch (Exception ex) { TxtStatus.Text = "归档 SolidWorks 资源失败：" + ex.Message; }
        }

        private sealed class SolidWorksElementSelectionItem
        {
            public long Id { get; set; }
            public string DisplayText { get; set; } = string.Empty;
        }

        private sealed class SolidWorksCategoryNode
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
            public string Name { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public int SortOrder { get; set; }
            public int Level { get; set; }
            public bool IsExpanded { get; set; }
            public List<SolidWorksCategoryNode> Children { get; set; } = new List<SolidWorksCategoryNode>();
            public string DisplayText => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
        }

        private sealed class SolidWorksCategoryEditorResult
        {
            public string Name;
            public string DisplayName;
            public int? SortOrder;
        }

        private static SolidWorksCategoryEditorResult PromptSolidWorksCategory(SolidWorksCategoryNode source, string title)
        {
            var name = new TextBox { Text = source?.Name ?? string.Empty };
            var displayName = new TextBox { Text = source?.DisplayName ?? string.Empty };
            var sortOrder = new TextBox { Text = source == null ? string.Empty : source.SortOrder.ToString() };
            var dialog = CreateEditorWindow(title, new[]
            {
                Tuple.Create("名称", (Control)name),
                Tuple.Create("显示名", (Control)displayName),
                Tuple.Create("排序", (Control)sortOrder)
            });
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(name.Text)) return null;
            return new SolidWorksCategoryEditorResult
            {
                Name = name.Text.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName.Text) ? name.Text.Trim() : displayName.Text.Trim(),
                SortOrder = int.TryParse(sortOrder.Text, out int value) && value > 0 ? value : (int?)null
            };
        }

        private void ShowSolidWorksCategoryResult(bool success, string message, string operation)
        {
            string text = success ? operation + "成功。" : operation + "失败：" + (string.IsNullOrWhiteSpace(message) ? "服务器未返回具体原因。" : message);
            TxtStatus.Text = text;
            Services.SolidWorksFileLogger.Info("SolidWorks " + text);
            if (!success) MessageBox.Show(text, "管理员模块", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void SetSolidWorksCategoryExpanded(SolidWorksCategoryNode node, bool expanded)
        {
            node.IsExpanded = expanded;
            foreach (SolidWorksCategoryNode child in node.Children) SetSolidWorksCategoryExpanded(child, expanded);
        }

        private bool IsSolidWorksAdministrator()
        {
            Services.SolidWorksFileLogger.Info("检查 SolidWorks 当前账号管理员权限。");
            string username = new SharedLoginSessionStore().Load().Username ?? string.Empty;
            username = username.Trim().ToLowerInvariant();
            // 客户端权限判断必须与服务器 StandardManagementAuthorization 保持一致。
            return username == "sa" || username == "admin" || username == "sysdba";
        }

        private async Task RefreshSolidWorksDepartmentsAsync()
        {
            Services.SolidWorksFileLogger.Start("刷新 SolidWorks 部门列表");
            if (_departmentUserApi == null)
            {
                Services.SolidWorksFileLogger.Skipped("刷新 SolidWorks 部门列表", "部门人员 API 尚未初始化");
                return;
            }
            try
            {
                // 刷新前记录当前部门，避免替换 ItemsSource 后丢失部门选择和下方人员表。
                int? selectedDepartmentId = (SW_DepartmentsGrid.SelectedItem as UnifiedDepartmentModel)?.Id;
                UnifiedDepartmentListResponse response = await _departmentUserApi.GetDepartmentsAsync();
                _solidWorksDepartments = response.Departments ?? new List<UnifiedDepartmentModel>();
                SW_DepartmentsGrid.ItemsSource = _solidWorksDepartments;

                UnifiedDepartmentModel selectedDepartment = selectedDepartmentId.HasValue
                    ? _solidWorksDepartments.FirstOrDefault(department => department.Id == selectedDepartmentId.Value)
                    : null;
                SW_DepartmentsGrid.SelectedItem = selectedDepartment;
                TxtStatus.Text = "已刷新部门，共 " + _solidWorksDepartments.Count + " 个。";
                Services.SolidWorksFileLogger.Complete("刷新 SolidWorks 部门列表；数量=" + _solidWorksDepartments.Count);
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Failed("刷新 SolidWorks 部门列表", ex);
                Services.SolidWorksFileLogger.Warning("SolidWorks 部门刷新失败：" + ex.Message);
                TxtStatus.Text = "部门刷新失败：" + ex.Message;
            }
        }

        private async Task RefreshSolidWorksUsersAsync()
        {
            Services.SolidWorksFileLogger.Start("刷新 SolidWorks 人员列表");
            UnifiedDepartmentModel department = SW_DepartmentsGrid.SelectedItem as UnifiedDepartmentModel;
            if (_departmentUserApi == null)
            {
                Services.SolidWorksFileLogger.Skipped("刷新 SolidWorks 人员列表", "部门人员 API 尚未初始化");
                return;
            }

            // 与 CAD 客户端保持一致：只有点击选中具体部门后，才加载下方用户表。
            if (department == null)
            {
                _solidWorksUsers = new List<UnifiedUserModel>();
                SW_UsersGrid.ItemsSource = null;
                Services.SolidWorksFileLogger.Skipped("刷新 SolidWorks 人员列表", "未选择部门");
                return;
            }

            int selectedDepartmentId = department.Id;
            try
            {
                List<UnifiedUserModel> users = await _departmentUserApi.GetUsersAsync(selectedDepartmentId);

                // 请求期间可能已经点击了其他部门，避免旧请求覆盖新部门的用户表。
                UnifiedDepartmentModel currentDepartment = SW_DepartmentsGrid.SelectedItem as UnifiedDepartmentModel;
                if (currentDepartment == null || currentDepartment.Id != selectedDepartmentId)
                    return;

                _solidWorksUsers = users ?? new List<UnifiedUserModel>();
                ApplySolidWorksUserFilter();
                Services.SolidWorksFileLogger.Complete("刷新 SolidWorks 人员列表；部门编号=" + selectedDepartmentId + "；数量=" + _solidWorksUsers.Count);
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Failed("刷新 SolidWorks 人员列表", ex);
                Services.SolidWorksFileLogger.Warning("SolidWorks 人员刷新失败：" + ex.Message);
                SW_UsersGrid.ItemsSource = null;
                TxtStatus.Text = "人员刷新失败：" + ex.Message;
            }
        }

        private void ApplySolidWorksUserFilter()
        {
            Services.SolidWorksFileLogger.Info("应用 SolidWorks 人员搜索筛选。");
            // XAML 初始化期间 TextChanged 可能早于 SW_UsersGrid 完成初始化而触发。
            if (SW_TxtSearchUser == null || SW_UsersGrid == null)
                return;

            string keyword = SW_TxtSearchUser.Text == "输入用户名搜索/分配" ? string.Empty : SW_TxtSearchUser.Text.Trim();
            SW_UsersGrid.ItemsSource = string.IsNullOrWhiteSpace(keyword)
                ? _solidWorksUsers
                : _solidWorksUsers.Where(user => (user.Username + " " + user.RealName + " " + user.Phone).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private async Task ExecuteSolidWorksMutationAsync(Func<Task<bool>> action, string successMessage)
        {
            Services.SolidWorksFileLogger.Start("执行 SolidWorks 部门/人员变更");
            try
            {
                if (await action())
                {
                    TxtStatus.Text = successMessage;
                    Services.SolidWorksFileLogger.Complete("执行 SolidWorks 部门/人员变更；结果=" + successMessage);
                    await RefreshSolidWorksDepartmentsAsync();
                    await RefreshSolidWorksUsersAsync();
                }
                else
                {
                    TxtStatus.Text = "操作失败，请检查服务器日志。";
                    Services.SolidWorksFileLogger.Warning("SolidWorks 部门/人员变更返回失败结果。");
                    MessageBox.Show(TxtStatus.Text, "部门/人员管理", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Failed("执行 SolidWorks 部门/人员变更", ex);
                Services.SolidWorksFileLogger.Warning("SolidWorks 部门/人员操作失败：" + ex.Message);
                MessageBox.Show("操作失败：" + ex.Message, "部门/人员管理", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        /// <summary>手动复制的 CAD 页面事件入口。CAD 专属操作在 SolidWorks 中暂不直接执行，统一记录提示避免误操作。</summary>
        private void SWCopiedCadActionNotAvailable(string action)
        {
            Services.SolidWorksFileLogger.Skipped("SolidWorks 页面操作入口：" + action, "该事件为 CAD 页面复制入口，当前不执行 CAD 专属逻辑");
        }

        private void AddFile_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(AddFile_Btn_Click));
        private void AddSubcategory_MenuItem_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(AddSubcategory_MenuItem_Click));
        private void ApplyProperties_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(ApplyProperties_Btn_Click));
        private async void BtnAddDept_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("新增 SolidWorks 部门");
            if (!_solidWorksAdministrator || _departmentUserApi == null) { Services.SolidWorksFileLogger.Skipped("新增 SolidWorks 部门", "当前账号无管理员权限或 API 未初始化"); return; }
            DepartmentEditorResult result = PromptDepartment(null, "新增部门");
            if (result == null) { Services.SolidWorksFileLogger.Skipped("新增 SolidWorks 部门", "用户取消输入"); return; }
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.AddDepartmentAsync(result.Name, result.DisplayName, result.Description, result.SortOrder, result.ManagerUserId)).Success, "部门新增成功。");
        }
        /// <summary>
        /// 处理“新增人员”按钮点击事件，弹出输入框收集新用户信息，并调用 API 新增用户。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BtnAddUserManaged_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("新增 SolidWorks 人员");
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(SW_DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) { Services.SolidWorksFileLogger.Skipped("新增 SolidWorks 人员", "权限、API 或部门选择不满足条件"); return; }
            UserEditorResult result = PromptUser(null, "新增人员");
            if (result == null) { Services.SolidWorksFileLogger.Skipped("新增 SolidWorks 人员", "用户取消输入"); return; }
            string departmentName = string.IsNullOrWhiteSpace(department.Name) ? department.DisplayName : department.Name;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.AddUserAsync(result.Username, result.Password, department.Id, departmentName, result.Role, result.IsActive, result.RealName, result.Gender, result.Phone, result.Email)).Success, "人员新增成功。");
        }
        /// <summary>
        /// 处理“分配人员到部门”按钮点击事件，获取输入的用户名并调用 API 将用户分配到选中的部门。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BtnAssignUser_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("分配 SolidWorks 人员到部门");
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(SW_DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) { Services.SolidWorksFileLogger.Skipped("分配 SolidWorks 人员到部门", "权限、API 或部门选择不满足条件"); return; }
            string username = SW_TxtSearchUser.Text.Trim();
            if (string.IsNullOrWhiteSpace(username) || username == "输入用户名搜索/分配") { Services.SolidWorksFileLogger.Skipped("分配 SolidWorks 人员到部门", "未输入有效用户名"); return; }
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.AssignUserToDepartmentAsync(username, department.Id)).Success, "用户分配成功。");
        }
        /// <summary>
        /// 处理“删除部门”按钮点击事件，确认后调用 API 删除选中的部门。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BtnDeleteDept_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("删除 SolidWorks 部门");
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(SW_DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) { Services.SolidWorksFileLogger.Skipped("删除 SolidWorks 部门", "权限、API 或部门选择不满足条件"); return; }
            if (MessageBox.Show("确认删除部门“" + department.DisplayName + "”？", "部门管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { Services.SolidWorksFileLogger.Skipped("删除 SolidWorks 部门", "用户取消确认"); return; }
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.DeleteDepartmentAsync(department.Id)).Success, "部门删除成功。");
        }
        /// <summary>
        /// 处理“删除人员”按钮点击事件，确认后调用 API 删除选中的用户。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BtnDeleteUserManaged_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("删除 SolidWorks 人员");
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(SW_UsersGrid.SelectedItem is UnifiedUserModel user)) { Services.SolidWorksFileLogger.Skipped("删除 SolidWorks 人员", "权限、API 或人员选择不满足条件"); return; }
            if (MessageBox.Show("确认删除用户“" + user.Username + "”？", "人员管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { Services.SolidWorksFileLogger.Skipped("删除 SolidWorks 人员", "用户取消确认"); return; }
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.DeleteUserAsync(user.Id)).Success, "人员删除成功。");
        }
        /// <summary>
        /// 处理“编辑部门”按钮点击事件，弹出输入框收集修改后的部门信息，并调用 API 更新部门。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BtnEditDept_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(SW_DepartmentsGrid.SelectedItem is UnifiedDepartmentModel department)) return;
            DepartmentEditorResult result = PromptDepartment(department, "编辑部门");
            if (result == null) return;
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.UpdateDepartmentAsync(department.Id, result.Name, result.DisplayName, result.Description, result.SortOrder, result.ManagerUserId, result.IsActive)).Success, "部门修改成功。");
        }

        private async void BtnEditUserManaged_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator || _departmentUserApi == null || !(SW_UsersGrid.SelectedItem is UnifiedUserModel user)) return;
            UserEditorResult result = PromptUser(user, "编辑人员");
            if (result == null) return;
            UnifiedDepartmentModel department = SW_DepartmentsGrid.SelectedItem as UnifiedDepartmentModel;
            string departmentName = department == null ? string.Empty : (string.IsNullOrWhiteSpace(department.Name) ? department.DisplayName : department.Name);
            await ExecuteSolidWorksMutationAsync(async () => (await _departmentUserApi.UpdateUserAsync(user.Id, result.Username, result.Role, result.IsActive, department?.Id, departmentName, result.Password, result.RealName, result.Gender, result.Phone, result.Email)).Success, "人员修改成功。");
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击刷新 SolidWorks 人员按钮。");
            await RefreshSolidWorksUsersAsync();
        }
        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("同步 SolidWorks 部门数据");
            if (!_solidWorksAdministrator || _departmentUserApi == null) { Services.SolidWorksFileLogger.Skipped("同步 SolidWorks 部门数据", "当前账号无管理员权限或 API 未初始化"); return; }
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
        private void SelectViewImage_Btn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "预览图片|*.png;*.jpg;*.jpeg" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                SW_viewFilePath.Text = dialog.FileName;
                LoadLocalSolidWorksPreviewImage(dialog.FileName);
                TxtStatus.Text = "已选择预览图片，可点击“完成添加”上传。";
                Services.SolidWorksFileLogger.Info("已选择 SolidWorks 本地预览图片；文件名=" + Path.GetFileName(dialog.FileName) + "。" );
            }
            catch (Exception ex)
            {
                SW_ViewImage.Source = null;
                Services.SolidWorksFileLogger.Warning("加载 SolidWorks 本地预览图片失败：" + ex.Message);
                TxtStatus.Text = "预览图片加载失败：" + ex.Message;
            }
        }

        /// <summary>加载管理员刚选择的本地预览图片，供上传前即时确认图片内容。</summary>
        private static BitmapImage CreateBitmapImageFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("预览图片文件不存在。", filePath);

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void LoadLocalSolidWorksPreviewImage(string filePath)
        {
            SW_ViewImage.Source = CreateBitmapImageFromFile(filePath);
            SW_ViewImage.Visibility = Visibility.Visible;
        }
        private void StroageFileDataGrid_Loaded(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(StroageFileDataGrid_Loaded));
        private void UploadClient_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(UploadClient_Btn_Click));
        private void 保存图层字典_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(保存图层字典_Click));
        /// <summary>打开 SolidWorks 日志目录，并在当前日志存在时选中当前日志文件。</summary>
        private void 查看日志文件夹按钮_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("查看 SolidWorks 日志文件夹");
            try
            {
                string logPath = Services.SolidWorksFileLogger.GetCurrentLogFilePath();
                string logDirectory = Services.SolidWorksFileLogger.GetLogDirectoryPath();
                bool fileExists = File.Exists(logPath);
                bool directoryExists = Directory.Exists(logDirectory);

                if (!directoryExists)
                {
                    MessageBox.Show("SolidWorks 日志目录尚未生成。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Services.SolidWorksFileLogger.Skipped("查看 SolidWorks 日志文件夹", "日志目录不存在");
                    return;
                }

                using (var process = new System.Diagnostics.Process())
                {
                    process.StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = "explorer.exe",
                        Arguments = fileExists ? "/select,\"" + logPath + "\"" : "\"" + logDirectory + "\""
                    };
                    process.Start();
                }

                Services.SolidWorksFileLogger.Complete("查看 SolidWorks 日志文件夹");
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Failed("查看 SolidWorks 日志文件夹", ex);
                MessageBox.Show("打开 SolidWorks 日志文件夹失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>使用系统默认程序打开当前小时的 SolidWorks 日志文件。</summary>
        private void 打开最新日志按钮_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Start("打开最新 SolidWorks 日志");
            try
            {
                string logPath = Services.SolidWorksFileLogger.GetCurrentLogFilePath();
                if (!File.Exists(logPath))
                {
                    MessageBox.Show("SolidWorks 日志文件尚未生成。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Services.SolidWorksFileLogger.Skipped("打开最新 SolidWorks 日志", "当前日志文件不存在");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
                Services.SolidWorksFileLogger.Complete("打开最新 SolidWorks 日志");
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Failed("打开最新 SolidWorks 日志", ex);
                MessageBox.Show("无法打开 SolidWorks 日志文件：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void 当前图纸图层_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(当前图纸图层_Click));
        private void 导出规范_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导出规范_Btn_Click));
        private void 导出模板_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导出模板_Btn_Click));
        private void 导入规范_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导入规范_Btn_Click));
        private void 导入模板_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(导入模板_Btn_Click));
        private void 规范树右键按下(object sender, MouseButtonEventArgs e) => SWCopiedCadActionNotAvailable(nameof(规范树右键按下));
        private void 规范树右键菜单_Opened(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(规范树右键菜单_Opened));
        private void 加载标准图层字典_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(加载标准图层字典_Click));
        private void 加载个人图层字典_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(加载个人图层字典_Click));
        /// <summary>异步读取当前 SolidWorks 日志并显示在日志页面中，避免阻塞界面。</summary>
        private async void 加载显示日志按钮_Click(object sender, RoutedEventArgs e)
        {
            await LoadSolidWorksLogAsync();
        }

        private async Task LoadSolidWorksLogAsync()
        {
            Services.SolidWorksFileLogger.Start("加载 SolidWorks 日志");
            try
            {
                string logPath = Services.SolidWorksFileLogger.GetCurrentLogFilePath();
                if (File.Exists(logPath))
                {
                    LogContentTextBlock.Text = await Task.Run(() => File.ReadAllText(logPath, Encoding.UTF8));
                }
                else
                {
                    LogContentTextBlock.Text = "SolidWorks 日志文件尚未生成。";
                }

                LogScrollViewer.ScrollToBottom();
                Services.SolidWorksFileLogger.Complete("加载 SolidWorks 日志");
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Failed("加载 SolidWorks 日志", ex);
                LogContentTextBlock.Text = "无法读取 SolidWorks 日志：" + ex.Message;
            }
        }

        /// <summary>应用 SolidWorks 日志页面设置并刷新当前日志显示。</summary>
        private async void 应用设置按钮_Click(object sender, RoutedEventArgs e)
        {
            await LoadSolidWorksLogAsync();
            MessageBox.Show("SolidWorks 日志设置已应用。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
        private void StroageFileDataGrid_TargetUpdated(object sender, DataTransferEventArgs e) => SWCopiedCadActionNotAvailable(nameof(StroageFileDataGrid_TargetUpdated));
        private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => SWCopiedCadActionNotAvailable(nameof(CategoryTreeView_SelectedItemChanged));
        private void SpecificationTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => SWCopiedCadActionNotAvailable(nameof(SpecificationTreeView_SelectedItemChanged));
        private async void DepartmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 事件绑定在复制后的 SolidWorks 部门表上，但操作语义与 CAD 完全一致。
            await RefreshSolidWorksUsersAsync();
        }

        private void SW_TxtSearchUser_TextChanged(object sender, TextChangedEventArgs e) => ApplySolidWorksUserFilter();

        private void SW_BtnSync_Click(object sender, RoutedEventArgs e) => BtnSync_Click(sender, e);
        private void SW_BtnAddDept_Click(object sender, RoutedEventArgs e) => BtnAddDept_Click(sender, e);
        private void SW_BtnEditDept_Click(object sender, RoutedEventArgs e) => BtnEditDept_Click(sender, e);
        private void SW_BtnDeleteDept_Click(object sender, RoutedEventArgs e) => BtnDeleteDept_Click(sender, e);
        private void SW_BtnRefresh_Click(object sender, RoutedEventArgs e) => BtnRefresh_Click(sender, e);
        private void SW_BtnAddUserManaged_Click(object sender, RoutedEventArgs e) => BtnAddUserManaged_Click(sender, e);
        private void SW_BtnEditUserManaged_Click(object sender, RoutedEventArgs e) => BtnEditUserManaged_Click(sender, e);
        private void SW_BtnDeleteUserManaged_Click(object sender, RoutedEventArgs e) => BtnDeleteUserManaged_Click(sender, e);
        private void SW_BtnAssignUser_Click(object sender, RoutedEventArgs e) => BtnAssignUser_Click(sender, e);

        private async void SW_LoadCategoryDatabase_Btn_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击【加载分类数据库】按钮，加载 CAD 与 SolidWorks 共用分类树。" );
            await RefreshSolidWorksCategoryTreeAsync();
        }

        /// <summary>右键菜单打开时同步当前节点，确保主分类和子分类操作使用正确的分类 ID。</summary>
        private void SW_CategoryContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            ContextMenu contextMenu = sender as ContextMenu;
            TreeViewItem treeViewItem = contextMenu?.PlacementTarget as TreeViewItem;
            if (treeViewItem == null) return;

            // 右键操作以菜单附着节点为准，避免使用上一次左键选中的分类。
            treeViewItem.IsSelected = true;
            treeViewItem.Focus();
            _selectedSolidWorksCategoryNode = treeViewItem.DataContext as SolidWorksCategoryNode;
            Services.SolidWorksFileLogger.Info("打开 SolidWorks 分类右键菜单；分类ID=" + (_selectedSolidWorksCategoryNode?.Id.ToString() ?? "无") + "。" );
        }

        private async void SW_NewCategory_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_solidWorksCategoryApi == null || !_solidWorksAdministrator) return;
            SolidWorksCategoryEditorResult result = PromptSolidWorksCategory(null, "新增主分类");
            if (result == null) return;
            SolidWorksCategoryApiService.CategoryMutationResponse response = await _solidWorksCategoryApi.AddCategoryAsync(result.Name, result.DisplayName, result.SortOrder);
            ShowSolidWorksCategoryResult(response.Success, response.Message, "新增主分类");
            if (response.Success) await RefreshSolidWorksCategoryTreeAsync();
        }
        private async void SW_AddSubcategory_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_solidWorksCategoryApi == null || !_solidWorksAdministrator || _selectedSolidWorksCategoryNode == null) return;
            SolidWorksCategoryNode parent = _selectedSolidWorksCategoryNode.Level == 0 ? _selectedSolidWorksCategoryNode : _solidWorksCategoryNodes.FirstOrDefault(node => node.Id == _selectedSolidWorksCategoryNode.ParentId);
            if (parent == null) return;
            SolidWorksCategoryEditorResult result = PromptSolidWorksCategory(null, "新增子分类");
            if (result == null) return;
            SolidWorksCategoryApiService.SubcategoryMutationResponse response = await _solidWorksCategoryApi.AddSubcategoryAsync(parent.Id, result.Name, result.DisplayName, result.SortOrder);
            ShowSolidWorksCategoryResult(response.Success, response.Message, "新增子分类");
            if (response.Success) await RefreshSolidWorksCategoryTreeAsync();
        }
        private async void SW_Edit_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_solidWorksCategoryApi == null || !_solidWorksAdministrator || _selectedSolidWorksCategoryNode == null) return;
            SolidWorksCategoryEditorResult result = PromptSolidWorksCategory(_selectedSolidWorksCategoryNode, "编辑分类");
            if (result == null) return;
            SolidWorksCategoryApiService.CategoryUpdateResponse response = await _solidWorksCategoryApi.UpdateCategoryAsync(_selectedSolidWorksCategoryNode.Id, result.Name, result.DisplayName, result.SortOrder);
            ShowSolidWorksCategoryResult(response.Success, response.Message, "编辑分类");
            if (response.Success) await RefreshSolidWorksCategoryTreeAsync();
        }
        private async void SW_Delete_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_solidWorksCategoryApi == null || !_solidWorksAdministrator || _selectedSolidWorksCategoryNode == null) return;
            if (MessageBox.Show("确认删除分类“" + _selectedSolidWorksCategoryNode.DisplayText + "”？", "分类管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SolidWorksCategoryApiService.CategoryDeleteResponse response = await _solidWorksCategoryApi.DeleteCategoryAsync(_selectedSolidWorksCategoryNode.Id, _selectedSolidWorksCategoryNode.Level > 0);
            ShowSolidWorksCategoryResult(response.Success, response.Message, "删除分类");
            if (response.Success) await RefreshSolidWorksCategoryTreeAsync();
        }
        private void SW_展开_折叠架构_Click(object sender, RoutedEventArgs e)
        {
            bool expand = _solidWorksCategoryNodes.Any(node => !node.IsExpanded);
            foreach (SolidWorksCategoryNode node in _solidWorksCategoryNodes) SetSolidWorksCategoryExpanded(node, expand);
        }
        private async void SW_刷新架构树按钮_Click(object sender, RoutedEventArgs e)
        {
            if (!_solidWorksAdministrator) return;
            await RefreshSolidWorksCategoryTreeAsync();
        }
        private void SW_SelectFile_Btn_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "SolidWorks 模型|*.sldprt;*.sldasm" };
            if (dialog.ShowDialog() == true) SW_filePath.Text = dialog.FileName;
        }

        private async void SW_DeleteGraphic_Btn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSolidWorksElement == null)
            {
                TxtStatus.Text = "请先在管理员模块分类树下选择统一构件。";
                return;
            }
            if (MessageBox.Show("确认归档当前构件的 SolidWorks 默认资源？", "SolidWorks 图元管理", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.ResourceListResponse response = await _solidWorksResourceApi.ListAsync(_selectedSolidWorksElement.Id);
                SolidWorksResourceApiService.ResourceDto resource = _selectedSolidWorksResource ?? response.Resources.FirstOrDefault();
                if (resource == null) { TxtStatus.Text = "当前统一构件没有 SolidWorks 资源。"; return; }
                await _solidWorksResourceApi.ArchiveResourceAsync(resource.Id);
                TxtStatus.Text = "SolidWorks 图元已归档。";
                await RefreshAdminSolidWorksResourcesAsync();
            }
            catch (Exception ex) { TxtStatus.Text = "归档 SolidWorks 图元失败：" + ex.Message; }
        }
        private void SW_ResetToInitial_Btn_Click(object sender, RoutedEventArgs e)
        {
            SW_filePath.Clear();
            SW_viewFilePath.Clear();
            SW_FileName.Clear();
            SW_FileSize.Clear();
            SW_ClientVersion.Clear();
            TxtStatus.Text = "已清空 SolidWorks 图元编辑输入。";
        }
        private async void SW_ApplyProperties_Btn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSolidWorksElement == null)
            {
                TxtStatus.Text = "请先在管理员模块中选择统一构件。";
                return;
            }
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                List<SolidWorksResourceApiService.PropertyValueRequest> values = (SW_CategoryPropertiesDataGrid.ItemsSource as IEnumerable<SolidWorksResourceApiService.PropertyValueDto> ?? Enumerable.Empty<SolidWorksResourceApiService.PropertyValueDto>())
                    .Select(property => new SolidWorksResourceApiService.PropertyValueRequest
                    {
                        PropertyDefinitionId = property.Id,
                        ValueText = property.ValueText ?? string.Empty
                    }).ToList();
                SolidWorksResourceApiService.OperationResponse propertyResponse = await _solidWorksResourceApi.SavePropertiesAsync(_selectedSolidWorksElement.Id, values);
                if (!propertyResponse.Success)
                {
                    TxtStatus.Text = "应用构件属性失败：" + propertyResponse.Message;
                    return;
                }
                if (_selectedSolidWorksResource == null)
                {
                    TxtStatus.Text = "统一构件属性已应用。当前未选择资源，未更新资源元数据。";
                    return;
                }
                SolidWorksResourceApiService.DetailsResponse response = await _solidWorksResourceApi.UpdateDetailsAsync(_selectedSolidWorksResource.Id, new SolidWorksResourceApiService.DetailsRequest
                {
                    ElementDefinitionId = _selectedSolidWorksResource.ElementDefinitionId,
                    ResourceType = _selectedSolidWorksResource.ResourceType,
                    ResourcePath = _selectedSolidWorksResource.ResourcePath,
                    FileHash = _selectedSolidWorksResource.FileHash,
                    ResourceName = string.IsNullOrWhiteSpace(SW_FileName.Text) ? _selectedSolidWorksResource.ResourceName : SW_FileName.Text.Trim(),
                    ConfigurationName = SW_ClientVersion.Text.Trim(),
                    Status = _selectedSolidWorksResource.Status,
                    IsDefault = _selectedSolidWorksResource.IsDefault
                });
                TxtStatus.Text = response.Success ? "统一构件属性和 SolidWorks 资源属性已应用。" : "应用资源属性失败：" + response.Message;
                await RefreshAdminSolidWorksResourcesAsync();
            }
            catch (Exception ex) { TxtStatus.Text = "应用 SolidWorks 图元属性失败：" + ex.Message; }
        }
        private async void SW_AddFile_Btn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSolidWorksElement == null || string.IsNullOrWhiteSpace(SW_filePath.Text))
            {
                TxtStatus.Text = "请先在管理员模块分类树中选择统一构件，并选择 SolidWorks 模型文件。";
                return;
            }
            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.ResourceResponse uploadResponse = await _solidWorksResourceApi.UploadModelAsync(_selectedSolidWorksElement.Id, SW_filePath.Text.Trim(), SW_FileName.Text.Trim(), SW_ClientVersion.Text.Trim());
                if (!uploadResponse.Success)
                {
                    TxtStatus.Text = "SolidWorks 图元上传失败：" + uploadResponse.Message;
                    return;
                }
                TxtStatus.Text = "SolidWorks 图元上传成功。";
                await RefreshAdminSolidWorksResourcesAsync();
                if (!string.IsNullOrWhiteSpace(SW_viewFilePath.Text))
                {
                    SolidWorksResourceApiService.ResourceDto resource = uploadResponse.Resource;
                    if (resource != null)
                    {
                        SolidWorksResourceApiService.AttachmentResponse previewResponse = await _solidWorksResourceApi.UploadPreviewAsync(resource.Id, SW_viewFilePath.Text.Trim(), "PREVIEW_IMAGE");
                        if (previewResponse.Success)
                        {
                            TxtStatus.Text = "SolidWorks 图元及预览图上传成功。";
                            LoadLocalSolidWorksPreviewImage(SW_viewFilePath.Text.Trim());
                        }
                        else
                        {
                            TxtStatus.Text = "模型已上传，但预览图失败：" + previewResponse.Message;
                        }
                    }
                }
            }
            catch (Exception ex) { TxtStatus.Text = "上传 SolidWorks 图元失败：" + ex.Message; }
        }
        private async void SW_ImportFromSelection_Btn_Click(object sender, RoutedEventArgs e)
        {
            Services.SolidWorksFileLogger.Info("点击 SolidWorks【添加当前图形入库】按钮。" );
            if (_selectedSolidWorksElement == null)
            {
                TxtStatus.Text = "请先选择分类和统一构件，再添加当前 SolidWorks 图形。";
                return;
            }

            if (SW == null)
            {
                TxtStatus.Text = "SolidWorks 应用程序对象尚未连接，无法读取当前图形。";
                return;
            }

            ModelDoc2 activeDocument = SW.ActiveDoc as ModelDoc2;
            if (activeDocument == null)
            {
                TxtStatus.Text = "当前没有打开的 SolidWorks 零件或装配体文档。";
                return;
            }

            int documentType = activeDocument.GetType();
            if (documentType != (int)swDocumentTypes_e.swDocPART && documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                TxtStatus.Text = "当前文档不是可入库的 SolidWorks 零件或装配体。";
                return;
            }

            string documentPath = activeDocument.GetPathName();
            if (string.IsNullOrWhiteSpace(documentPath) || !File.Exists(documentPath))
            {
                TxtStatus.Text = "当前 SolidWorks 文档尚未保存，请先保存为 SLDPRT 或 SLDASM 文件。";
                return;
            }

            string extension = Path.GetExtension(documentPath).ToLowerInvariant();
            if (extension != ".sldprt" && extension != ".sldasm")
            {
                TxtStatus.Text = "当前文档文件类型不是 SLDPRT 或 SLDASM。";
                return;
            }

            SW_filePath.Text = documentPath;
            if (string.IsNullOrWhiteSpace(SW_FileName.Text))
                SW_FileName.Text = Path.GetFileNameWithoutExtension(documentPath);

            try
            {
                _solidWorksResourceApi = _solidWorksResourceApi ?? new SolidWorksResourceApiService(new SharedLoginSessionStore().Load());
                SolidWorksResourceApiService.ResourceResponse uploadResponse = await _solidWorksResourceApi.UploadModelAsync(
                    _selectedSolidWorksElement.Id,
                    documentPath,
                    SW_FileName.Text.Trim(),
                    SW_ClientVersion.Text.Trim());

                if (!uploadResponse.Success)
                {
                    TxtStatus.Text = "当前 SolidWorks 图形入库失败：" + uploadResponse.Message;
                    return;
                }

                TxtStatus.Text = "当前 SolidWorks 图形已入库。GLB/GLTF 三维预览需要另行上传。";
                await RefreshAdminSolidWorksResourcesAsync();
            }
            catch (Exception ex)
            {
                Services.SolidWorksFileLogger.Warning("当前 SolidWorks 图形入库失败：" + ex.Message);
                TxtStatus.Text = "当前 SolidWorks 图形入库失败：" + ex.Message;
            }
        }
        private void SW_上传客户端_Btn_Click(object sender, RoutedEventArgs e) => SWCopiedCadActionNotAvailable(nameof(SW_上传客户端_Btn_Click));

        private sealed class DepartmentEditorResult
        {
            public string Name;
            public string DisplayName;
            public string Description;
            public int SortOrder;
            public int? ManagerUserId;
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
            var managerUserId = new TextBox { Text = source?.ManagerUserId?.ToString() ?? string.Empty };
            var dialog = CreateEditorWindow(title, new[] { Tuple.Create("名称", (Control)name), Tuple.Create("显示名", (Control)displayName), Tuple.Create("说明", (Control)description), Tuple.Create("排序", (Control)sortOrder), Tuple.Create("负责人ID", (Control)managerUserId) });
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(name.Text)) return null;
            int managerId;
            return new DepartmentEditorResult { Name = name.Text.Trim(), DisplayName = string.IsNullOrWhiteSpace(displayName.Text) ? name.Text.Trim() : displayName.Text.Trim(), Description = description.Text.Trim(), SortOrder = int.TryParse(sortOrder.Text, out int value) ? value : 0, ManagerUserId = int.TryParse(managerUserId.Text, out managerId) && managerId > 0 ? managerId : (int?)null, IsActive = source == null || source.IsActive };
        }
        /// <summary>
        /// 弹出输入框收集用户信息，用于新增或编辑人员。返回 null 表示用户取消输入。
        /// </summary>
        /// <param name="source"></param>
        /// <param name="title"></param>
        /// <returns></returns>
        private static UserEditorResult PromptUser(UnifiedUserModel source, string title)
        {
            var username = new TextBox { Text = source?.Username ?? string.Empty };
            var password = new PasswordBox();
            var realName = new TextBox { Text = source?.RealName ?? string.Empty };
            var gender = new TextBox { Text = source?.Gender ?? string.Empty };
            // 角色只能从服务器支持的两种角色中选择，新增人员默认使用普通用户角色。
            var role = new ComboBox { IsEditable = false, SelectedValuePath = "Tag" };
            role.Items.Add(new ComboBoxItem { Content = "用户（user）", Tag = "user" });
            role.Items.Add(new ComboBoxItem { Content = "管理员（administrator）", Tag = "administrator" });
            string roleValue = string.Equals(source?.Role, "administrator", StringComparison.OrdinalIgnoreCase) ? "administrator" : "user";
            role.SelectedValue = roleValue;
            var phone = new TextBox { Text = source?.Phone ?? string.Empty };
            var email = new TextBox { Text = source?.Email ?? string.Empty };
            var isActive = new CheckBox { Content = "启用账号", IsChecked = source == null || source.IsActive, Margin = new Thickness(0, 2, 0, 6) };
            var dialog = CreateEditorWindow(title, new[] { Tuple.Create("账号", (Control)username), Tuple.Create("密码", (Control)password), Tuple.Create("姓名", (Control)realName), Tuple.Create("性别", (Control)gender), Tuple.Create("角色", (Control)role), Tuple.Create("电话", (Control)phone), Tuple.Create("邮箱", (Control)email), Tuple.Create("状态", (Control)isActive) });
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(username.Text)) return null;
            return new UserEditorResult { Username = username.Text.Trim(), Password = password.Password, RealName = realName.Text.Trim(), Gender = gender.Text.Trim(), Role = role.SelectedValue as string ?? "user", Phone = phone.Text.Trim(), Email = email.Text.Trim(), IsActive = isActive.IsChecked == true };
        }
        /// <summary>
        /// 创建一个通用的编辑窗口，用于收集部门或人员信息。根据传入的字段动态生成输入控件，并提供确定和取消按钮。
        /// </summary>
        /// <param name="title">窗口标题</param>
        /// <param name="fields">要显示的字段及其对应的输入控件</param>
        /// <returns>返回创建的窗口对象</returns>
        private static Window CreateEditorWindow(string title, IEnumerable<Tuple<string, Control>> fields)
        {
            // 按实际字段数量自动计算窗口高度，避免新增人员字段较多时裁剪底部按钮。
            var dialog = new Window { Title = title, Width = 360, SizeToContent = SizeToContent.Height, MinHeight = 260, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize };
            var panel = new StackPanel { Margin = new Thickness(12) };
            foreach (Tuple<string, Control> field in fields) { panel.Children.Add(new TextBlock { Text = field.Item1 }); panel.Children.Add(field.Item2); field.Item2.Margin = new Thickness(0, 2, 0, 6); }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "确定", Width = 70, Margin = new Thickness(4, 0, 0, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 70, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (sender, args) => dialog.DialogResult = true;
            cancel.Click += (sender, args) => dialog.DialogResult = false;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            return dialog;
        }
    }
}

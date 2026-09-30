using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.DisplayPages
{
    public partial class CategoryPage : Page
    {
        private readonly SwUserSession? _session;
        private readonly SwApiClient? _client;

        public CategoryPage(SwUserSession? session)
        {
            InitializeComponent();
            _session = session;
            _client = session == null ? null : new SwApiClient(session.Settings);
            Loaded += async (_, _) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            if (_session == null || !_session.IsSignedIn || _client == null)
            {
                StatusTextBlock.Text = "当前没有有效登录会话。";
                return;
            }

            try
            {
                IsEnabled = false;
                StatusTextBlock.Text = "正在加载服务器分类...";
                CategoryTreeResponse response = await _client.GetJsonAsync<CategoryTreeResponse>("api/categories/tree");
                if (!response.Success)
                {
                    StatusTextBlock.Text = string.IsNullOrWhiteSpace(response.Message) ? "分类查询失败。" : response.Message;
                    return;
                }

                response.Categories = response.Categories ?? new System.Collections.Generic.List<CategoryDto>();
                response.Subcategories = response.Subcategories ?? new System.Collections.Generic.List<SubcategoryDto>();
                CategoryTreeView.Items.Clear();
                foreach (CategoryDto category in response.Categories.OrderBy(item => item.SortOrder))
                {
                    var root = new TreeViewItem { Header = category.DisplayName, Tag = category.Id };
                    foreach (SubcategoryDto child in response.Subcategories
                        .Where(item => item.ParentId == category.Id)
                        .OrderBy(item => item.SortOrder))
                    {
                        root.Items.Add(new TreeViewItem { Header = child.DisplayName, Tag = child.Id });
                    }
                    CategoryTreeView.Items.Add(root);
                }

                StatusTextBlock.Text = $"已加载 {response.Categories.Count} 个主分类和 {response.Subcategories.Count} 个子分类。数据来自统一服务器。";
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

    [System.Runtime.Serialization.DataContract]
    public sealed class CategoryTreeResponse
    {
        [System.Runtime.Serialization.DataMember(Name = "success")]
        public bool Success { get; set; }
        [System.Runtime.Serialization.DataMember(Name = "message")]
        public string Message { get; set; } = string.Empty;
        [System.Runtime.Serialization.DataMember(Name = "categories")]
        public System.Collections.Generic.List<CategoryDto> Categories { get; set; } = new System.Collections.Generic.List<CategoryDto>();
        [System.Runtime.Serialization.DataMember(Name = "subcategories")]
        public System.Collections.Generic.List<SubcategoryDto> Subcategories { get; set; } = new System.Collections.Generic.List<SubcategoryDto>();
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class CategoryDto
    {
        [System.Runtime.Serialization.DataMember(Name = "id")]
        public int Id { get; set; }
        [System.Runtime.Serialization.DataMember(Name = "displayName")]
        public string DisplayName { get; set; } = string.Empty;
        [System.Runtime.Serialization.DataMember(Name = "sortOrder")]
        public int SortOrder { get; set; }
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class SubcategoryDto
    {
        [System.Runtime.Serialization.DataMember(Name = "id")]
        public int Id { get; set; }
        [System.Runtime.Serialization.DataMember(Name = "parentId")]
        public int ParentId { get; set; }
        [System.Runtime.Serialization.DataMember(Name = "displayName")]
        public string DisplayName { get; set; } = string.Empty;
        [System.Runtime.Serialization.DataMember(Name = "sortOrder")]
        public int SortOrder { get; set; }
    }
}

using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>SolidWorks 管理员模块使用的分类树 API；所有请求均通过统一登录会话访问服务器。</summary>
    public sealed class SolidWorksCategoryApiService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly SharedLoginSession _session;

        public SolidWorksCategoryApiService(SharedLoginSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public Task<CategoryTreeResponse> GetTreeAsync(CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<CategoryTreeResponse>(HttpMethod.Get, "api/categories/tree", null, cancellationToken);

        public Task<CategoryMutationResponse> AddCategoryAsync(string name, string displayName, int? sortOrder, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<CategoryMutationResponse>(HttpMethod.Post, "api/categories", new AddCategoryRequest
            {
                Name = name,
                DisplayName = displayName,
                SortOrder = sortOrder
            }, cancellationToken);

        public Task<SubcategoryMutationResponse> AddSubcategoryAsync(int parentId, string name, string displayName, int? sortOrder, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<SubcategoryMutationResponse>(HttpMethod.Post, "api/categories/" + parentId + "/subcategories", new AddSubcategoryRequest
            {
                Name = name,
                DisplayName = displayName,
                SortOrder = sortOrder
            }, cancellationToken);

        public Task<CategoryUpdateResponse> UpdateCategoryAsync(int id, string name, string displayName, int? sortOrder, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<CategoryUpdateResponse>(HttpMethod.Put, "api/categories/" + id, new UpdateCategoryRequest
            {
                Name = name,
                DisplayName = displayName,
                SortOrder = sortOrder
            }, cancellationToken);

        public Task<CategoryDeleteResponse> DeleteCategoryAsync(int id, bool isSubcategory, CancellationToken cancellationToken = default(CancellationToken))
        {
            string path = isSubcategory ? "api/categories/subcategories/" + id : "api/categories/" + id;
            return SendAsync<CategoryDeleteResponse>(HttpMethod.Delete, path, null, cancellationToken);
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            SolidWorksFileLogger.Start("调用 SolidWorks 分类 API：" + method.Method + " " + path);
            string host = (_session.ServerHost ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                SolidWorksFileLogger.Skipped("调用 SolidWorks 分类 API：" + path, "统一登录会话中没有服务器地址");
                throw new InvalidOperationException("统一登录会话中没有服务器地址。");
            }
            string baseUrl = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host : "http://" + host;
            string url = baseUrl.TrimEnd('/') + ":" + (_session.ApiPort > 0 ? _session.ApiPort : 10010) + "/" + path;
            using (var request = new HttpRequestMessage(method, url))
            {
                request.Headers.TryAddWithoutValidation("X-Client-Platform", "SOLIDWORKS");
                if (!string.IsNullOrWhiteSpace(_session.AccessToken)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _session.AccessToken);
                if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    SolidWorksFileLogger.Info("SolidWorks 分类 API 响应；路径=" + path + "；HTTP=" + (int)response.StatusCode + "；响应长度=" + text.Length + "；耗时毫秒=" + stopwatch.ElapsedMilliseconds + "。" );
                    if (!response.IsSuccessStatusCode)
                    {
                        SolidWorksFileLogger.Error("SolidWorks 分类 API 请求失败；路径=" + path + "；HTTP=" + (int)response.StatusCode + "。" );
                        throw new InvalidOperationException("分类接口请求失败，HTTP " + (int)response.StatusCode + "。");
                    }
                    T result = JsonConvert.DeserializeObject<T>(text);
                    if (result == null)
                    {
                        SolidWorksFileLogger.Error("SolidWorks 分类 API 返回为空；路径=" + path + "。" );
                        throw new InvalidOperationException("分类接口返回为空。");
                    }
                    SolidWorksFileLogger.Complete("调用 SolidWorks 分类 API：" + method.Method + " " + path);
                    return result;
                }
            }
        }

        public sealed class CategoryTreeResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public List<CategoryDto> Categories { get; set; } = new List<CategoryDto>(); public List<SubcategoryDto> Subcategories { get; set; } = new List<SubcategoryDto>(); }
        public sealed class CategoryDto { public int Id { get; set; } public string Name { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public int SortOrder { get; set; } }
        public sealed class SubcategoryDto { public int Id { get; set; } public int ParentId { get; set; } public string Name { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public int SortOrder { get; set; } }
        public sealed class CategoryMutationResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public CategoryDto Category { get; set; } }
        public sealed class SubcategoryMutationResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public SubcategoryDto Subcategory { get; set; } }
        public sealed class CategoryUpdateResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public int UpdatedId { get; set; } public string Name { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public int SortOrder { get; set; } }
        public sealed class CategoryDeleteResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public int DeletedId { get; set; } }
        private sealed class AddCategoryRequest { public string Name { get; set; } public string DisplayName { get; set; } public int? SortOrder { get; set; } }
        private sealed class AddSubcategoryRequest { public string Name { get; set; } public string DisplayName { get; set; } public int? SortOrder { get; set; } }
        private sealed class UpdateCategoryRequest { public string Name { get; set; } public string DisplayName { get; set; } public int? SortOrder { get; set; } }
    }
}

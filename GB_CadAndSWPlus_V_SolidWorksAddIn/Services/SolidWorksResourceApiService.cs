using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.Shared.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>SolidWorks 资源库客户端 API；所有文件均通过统一登录会话上传到服务器受控目录。</summary>
    public sealed class SolidWorksResourceApiService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private readonly SharedLoginSession _session;

        public SolidWorksResourceApiService(SharedLoginSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>下载 SolidWorks 模型到客户端临时缓存，服务器物理路径不会暴露给客户端。</summary>
        public async Task<byte[]> DownloadModelAsync(long resourceId, CancellationToken cancellationToken = default(CancellationToken))
        {
            string operation = "下载 SolidWorks 模型；资源ID=" + resourceId;
            SolidWorksFileLogger.Start(operation);
            try
            {
                using (var request = CreateRequest(HttpMethod.Get, "api/solidworks-resources/resources/" + resourceId + "/model"))
                using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    SolidWorksFileLogger.Info("SolidWorks 模型下载接口已返回；路径=" + request.RequestUri.AbsolutePath + "；HTTP=" + (int)response.StatusCode + "；内容类型=" + (response.Content.Headers.ContentType?.MediaType ?? "未知") + "；内容长度=" + (response.Content.Headers.ContentLength.HasValue ? response.Content.Headers.ContentLength.Value.ToString() : "未知") + "。" );
                    if (!response.IsSuccessStatusCode)
                    {
                        SolidWorksFileLogger.Error("SolidWorks 模型下载失败；资源ID=" + resourceId + "；HTTP=" + (int)response.StatusCode + "。");
                        throw new InvalidOperationException("SolidWorks 模型下载失败，HTTP " + (int)response.StatusCode + "。");
                    }
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    SolidWorksFileLogger.Complete(operation + "；字节数=" + bytes.Length);
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Failed(operation, ex);
                throw;
            }
        }

        /// <summary>替换已选资源对应的模型文件，并保留原资源 ID。</summary>
        public async Task<ResourceResponse> ReplaceModelAsync(long resourceId, string filePath, string resourceName, string configurationName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("SolidWorks 模型文件不存在。", filePath);
            using (var content = new MultipartFormDataContent())
            using (var stream = File.OpenRead(filePath))
            using (var fileContent = new StreamContent(stream))
            {
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", Path.GetFileName(filePath));
                content.Add(new StringContent(resourceName ?? string.Empty), "resourceName");
                content.Add(new StringContent(configurationName ?? string.Empty), "configurationName");
                return await SendMultipartAsync<ResourceResponse>(HttpMethod.Put, "api/solidworks-resources/resources/" + resourceId + "/model", content, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task<ResourceListResponse> ListAsync(long elementId, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<ResourceListResponse>(HttpMethod.Get, "api/solidworks-resources/elements/" + elementId, null, cancellationToken);

        /// <summary>按管理员模块当前选中的统一分类查询构件定义。</summary>
        public Task<ElementListResponse> ListElementsByCategoryAsync(int categoryId, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<ElementListResponse>(HttpMethod.Get, "api/elements?categoryId=" + categoryId + "&platform=SOLIDWORKS", null, cancellationToken);

        public async Task<ResourceResponse> UploadModelAsync(long elementId, string filePath, string resourceName, string configurationName, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("SolidWorks 模型文件不存在。", filePath);
            using (var content = new MultipartFormDataContent())
            using (var stream = File.OpenRead(filePath))
            using (var fileContent = new StreamContent(stream))
            {
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", Path.GetFileName(filePath));
                content.Add(new StringContent(resourceName ?? string.Empty), "resourceName");
                content.Add(new StringContent(configurationName ?? string.Empty), "configurationName");
                return await SendMultipartAsync<ResourceResponse>(HttpMethod.Post, "api/solidworks-resources/elements/" + elementId + "/model", content, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<AttachmentResponse> UploadPreviewAsync(long resourceId, string filePath, string attachmentType, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("SolidWorks 预览文件不存在。", filePath);
            using (var content = new MultipartFormDataContent())
            using (var stream = File.OpenRead(filePath))
            using (var fileContent = new StreamContent(stream))
            {
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "file", Path.GetFileName(filePath));
                content.Add(new StringContent(attachmentType ?? string.Empty), "attachmentType");
                return await SendMultipartAsync<AttachmentResponse>(HttpMethod.Post, "api/solidworks-resources/resources/" + resourceId + "/attachments", content, cancellationToken).ConfigureAwait(false);
            }
        }

        public Task<DetailsResponse> UpdateDetailsAsync(long resourceId, DetailsRequest request, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<DetailsResponse>(HttpMethod.Put, "api/solidworks-resources/resources/" + resourceId + "/details", request, cancellationToken);

        public Task<OperationResponse> ArchiveResourceAsync(long resourceId, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<OperationResponse>(HttpMethod.Delete, "api/solidworks-resources/resources/" + resourceId, null, cancellationToken);

        public Task<OperationResponse> ArchiveAttachmentAsync(long attachmentId, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<OperationResponse>(HttpMethod.Delete, "api/solidworks-resources/attachments/" + attachmentId, null, cancellationToken);

        public Task<AttachmentListResponse> ListAttachmentsAsync(long resourceId, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<AttachmentListResponse>(HttpMethod.Get, "api/solidworks-resources/resources/" + resourceId + "/attachments", null, cancellationToken);

        public async Task<byte[]> DownloadAttachmentAsync(long attachmentId, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var request = CreateRequest(HttpMethod.Get, "api/solidworks-resources/attachments/" + attachmentId + "/content"))
            using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException("SolidWorks 预览附件下载失败，HTTP " + (int)response.StatusCode + "。");
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        /// <summary>读取当前统一构件的共用属性。</summary>
        public Task<PropertyListResponse> ListPropertiesAsync(long elementId, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<PropertyListResponse>(HttpMethod.Get, "api/elements/" + elementId + "/properties", null, cancellationToken);

        /// <summary>保存当前统一构件的共用属性值。</summary>
        public Task<OperationResponse> SavePropertiesAsync(long elementId, List<PropertyValueRequest> values, CancellationToken cancellationToken = default(CancellationToken))
            => SendJsonAsync<OperationResponse>(HttpMethod.Put, "api/elements/" + elementId + "/properties", values, cancellationToken);

        private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
        {
            using (var request = CreateRequest(method, path))
            {
                if (body != null)
                    request.Content = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, "application/json");
                return await SendAsync<T>(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<T> SendMultipartAsync<T>(HttpMethod method, string path, HttpContent content, CancellationToken cancellationToken)
        {
            using (var request = CreateRequest(method, path))
            {
                request.Content = content;
                return await SendAsync<T>(request, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string operation = "调用 SolidWorks 资源 API：" + request.Method.Method + " " + request.RequestUri.AbsolutePath;
            SolidWorksFileLogger.Start(operation);
            try
            {
                using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    SolidWorksFileLogger.Info("SolidWorks 资源 API 响应；路径=" + request.RequestUri.AbsolutePath + "；HTTP=" + (int)response.StatusCode + "；响应长度=" + text.Length + "；耗时毫秒=" + stopwatch.ElapsedMilliseconds + "。" );
                    if (!response.IsSuccessStatusCode)
                    {
                        SolidWorksFileLogger.Error("SolidWorks 资源 API 请求失败；路径=" + request.RequestUri.AbsolutePath + "；HTTP=" + (int)response.StatusCode + "。" );
                        throw new InvalidOperationException("SolidWorks 资源接口请求失败，HTTP " + (int)response.StatusCode + "。");
                    }
                    T result = JsonConvert.DeserializeObject<T>(text);
                    if (result == null)
                        throw new InvalidOperationException("SolidWorks 资源接口返回为空。");
                    SolidWorksFileLogger.Complete(operation + "；耗时毫秒=" + stopwatch.ElapsedMilliseconds);
                    return result;
                }
            }
            catch (Exception ex)
            {
                SolidWorksFileLogger.Failed(operation, ex);
                throw;
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            string host = (_session.ServerHost ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("统一登录会话中没有服务器地址。");
            string baseUrl = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host : "http://" + host;
            string url = baseUrl.TrimEnd('/') + ":" + (_session.ApiPort > 0 ? _session.ApiPort : 10010) + "/" + path;
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("X-Client-Platform", "SOLIDWORKS");
            // 服务器管理员接口通过操作人请求头执行权限校验；不能只发送 Bearer Token。
            if (!string.IsNullOrWhiteSpace(_session.Username)) request.Headers.TryAddWithoutValidation("X-Operator-Name", _session.Username.Trim());
            if (!string.IsNullOrWhiteSpace(_session.AccessToken)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _session.AccessToken);
            return request;
        }

        public sealed class ResourceListResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public List<ResourceDto> Resources { get; set; } = new List<ResourceDto>(); }
        public sealed class ElementListResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public List<ElementDto> Elements { get; set; } = new List<ElementDto>(); }
        public sealed class ElementDto { public long Id { get; set; } public string ElementCode { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public int? CategoryId { get; set; } public string Status { get; set; } = string.Empty; }
        public sealed class ResourceResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public ResourceDto Resource { get; set; } }
        public sealed class ResourceDto { public long Id { get; set; } public long ElementDefinitionId { get; set; } public string Platform { get; set; } = string.Empty; public string ResourceType { get; set; } = string.Empty; public string ResourceName { get; set; } = string.Empty; public string OriginalFileName { get; set; } = string.Empty; public string ResourcePath { get; set; } = string.Empty; public string FileHash { get; set; } = string.Empty; public int Version { get; set; } public string ConfigurationName { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public bool IsDefault { get; set; } }
        public sealed class DetailsRequest { public long ElementDefinitionId { get; set; } public string ResourceType { get; set; } public string ResourcePath { get; set; } public string FileHash { get; set; } public string ResourceName { get; set; } public string ConfigurationName { get; set; } public string Status { get; set; } public bool IsDefault { get; set; } }
        public sealed class DetailsResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public ResourceDto Resource { get; set; } }
        public sealed class AttachmentResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public AttachmentDto Attachment { get; set; } }
        public sealed class AttachmentListResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public List<AttachmentDto> Attachments { get; set; } = new List<AttachmentDto>(); }
        public sealed class AttachmentDto { public long Id { get; set; } public long ResourceId { get; set; } public string AttachmentType { get; set; } = string.Empty; public string OriginalFileName { get; set; } = string.Empty; public long FileSize { get; set; } public int Version { get; set; } public string Status { get; set; } = string.Empty; }
        public sealed class OperationResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; }
        public sealed class PropertyListResponse { public bool Success { get; set; } public string Message { get; set; } = string.Empty; public List<PropertyValueDto> Data { get; set; } = new List<PropertyValueDto>(); }
        public sealed class PropertyValueDto { public long Id { get; set; } public long ElementDefinitionId { get; set; } public string PropertyCode { get; set; } = string.Empty; public string PropertyName { get; set; } = string.Empty; public string DataType { get; set; } = string.Empty; public string Unit { get; set; } = string.Empty; public bool IsRequired { get; set; } public int SortOrder { get; set; } public string ValueText { get; set; } = string.Empty; public decimal? ValueNumber { get; set; } public bool? ValueBool { get; set; } }
        public sealed class PropertyValueRequest { public long PropertyDefinitionId { get; set; } public string ValueText { get; set; } = string.Empty; }
    }
}

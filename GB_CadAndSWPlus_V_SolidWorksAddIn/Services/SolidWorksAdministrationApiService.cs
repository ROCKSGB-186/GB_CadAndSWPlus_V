using GB_CadAndSWPlus_V.Shared.Models;
using GB_CadAndSWPlus_V.Shared.Services;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>SolidWorks 管理页的统一 API 客户端；所有请求均使用共享登录会话的服务器和端口。</summary>
    public sealed class SolidWorksAdministrationApiService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly SharedLoginSession _session;

        public SolidWorksAdministrationApiService(SharedLoginSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public Task<SolidWorksDepartmentResponse> GetDepartmentsAsync(CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<SolidWorksDepartmentResponse>(HttpMethod.Get, "api/departments", null, cancellationToken);

        public Task<SolidWorksUserResponse> GetUsersAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<SolidWorksUserResponse>(HttpMethod.Get, "api/users?departmentId=" + departmentId, null, cancellationToken);

        public Task<SolidWorksMutationResult> AddDepartmentAsync(string name, string displayName, string description, int sortOrder, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<SolidWorksMutationResult>(HttpMethod.Post, "api/departments", new { Name = name, DisplayName = displayName, Description = description, SortOrder = sortOrder, IsActive = true }, cancellationToken);

        public Task<SolidWorksMutationResult> DeleteDepartmentAsync(int id, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<SolidWorksMutationResult>(HttpMethod.Delete, "api/departments/" + id, null, cancellationToken);

        public Task<SolidWorksMutationResult> DeleteUserAsync(int id, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<SolidWorksMutationResult>(HttpMethod.Delete, "api/users/" + id, null, cancellationToken);

        public Task<SolidWorksMutationResult> SetConfigAsync(string key, string value, CancellationToken cancellationToken = default(CancellationToken))
            => SendAsync<SolidWorksMutationResult>(HttpMethod.Put, "api/system-config/" + Uri.EscapeDataString(key), new { Value = value ?? string.Empty }, cancellationToken);

        public async Task<string> GetConfigAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            SolidWorksConfigResponse result = await SendAsync<SolidWorksConfigResponse>(HttpMethod.Get, "api/system-config/" + Uri.EscapeDataString(key), null, cancellationToken).ConfigureAwait(false);
            return result.Value ?? string.Empty;
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken)
        {
            string host = (_session.ServerHost ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("统一登录会话中没有服务器地址。");
            string baseUrl = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? host : "http://" + host;
            string url = baseUrl.TrimEnd('/') + ":" + (_session.ApiPort > 0 ? _session.ApiPort : 10010) + "/" + path.TrimStart('/');
            SolidWorksFileLogger.Info("SolidWorks 管理 API 请求开始；方法=" + method.Method + "；路径=" + path);
            using (var request = new HttpRequestMessage(method, url))
            {
                request.Headers.TryAddWithoutValidation("X-Client-Platform", "SOLIDWORKS");
                if (!string.IsNullOrWhiteSpace(_session.AccessToken)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _session.AccessToken);
                if (body != null) request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    SolidWorksFileLogger.Info("SolidWorks 管理 API 请求完成；路径=" + path + "；HTTP=" + (int)response.StatusCode + "；响应长度=" + text.Length);
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("服务器接口请求失败，HTTP " + (int)response.StatusCode + "。");
                    T result = JsonConvert.DeserializeObject<T>(text);
                    if (result == null) throw new InvalidOperationException("服务器返回空响应。");
                    return result;
                }
            }
        }
    }
}

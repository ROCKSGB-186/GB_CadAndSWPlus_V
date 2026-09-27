using GB_CadAndSWPlus_V.FunctionalMethod;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.Helpers
{
    /// <summary>
    /// API 健康检查客户端。
    /// </summary>
    public sealed class HealthApiService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = ApiEndpoint.ConnectTimeout
        };

        private sealed class HealthResponse
        {
            public bool Success { get; set; }
            public string Service { get; set; } = string.Empty;
            public string Database { get; set; } = string.Empty;
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl;
            try
            {
                requestUrl = ApiEndpoint.Build("api/health");
                using (HttpResponseMessage response = await HttpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    HealthResponse result = JsonConvert.DeserializeObject<HealthResponse>(responseBody);
                    bool available = response.IsSuccessStatusCode
                        && result != null
                        && result.Success
                        && string.Equals(result.Service, "ok", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(result.Database, "ok", StringComparison.OrdinalIgnoreCase);

                    LogManager.Instance.LogInfo($"API 健康检查：HTTP {(int)response.StatusCode}，可用={available}");
                    return available;
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"API 健康检查失败：{ex.Message}");
                return false;
            }
        }
    }
}

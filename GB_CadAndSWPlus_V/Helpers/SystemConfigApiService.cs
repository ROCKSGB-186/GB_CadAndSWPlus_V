using GB_CadAndSWPlus_V.FunctionalMethod;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.Helpers
{
    /// <summary>
    /// 系统配置服务器接口客户端。
    /// 返回 null 表示 API 失败，调用方根据具体业务决定是否使用默认值。
    /// </summary>
    public sealed class SystemConfigApiService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private sealed class ConfigResponse
        {
            public bool Success { get; set; }
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }

        private sealed class SetRequest
        {
            public string Value { get; set; } = string.Empty;
        }

        private sealed class MutationResponse
        {
            public bool Success { get; set; }
            public int AffectedRows { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        public async Task<string> GetAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl = ApiEndpoint.Build("api/system-config/" + Uri.EscapeDataString(key ?? string.Empty));
            try
            {
                using (HttpResponseMessage response = await HttpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    LogManager.Instance.LogInfo($"系统配置接口响应：HTTP {(int)response.StatusCode} {response.ReasonPhrase}, BodyLength={responseBody.Length}, Key={key}");
                    ConfigResponse result = JsonConvert.DeserializeObject<ConfigResponse>(responseBody);
                    if (result == null || !response.IsSuccessStatusCode || !result.Success)
                        throw new InvalidOperationException(result?.Message ?? $"系统配置读取接口返回无效，HTTP {(int)response.StatusCode}。");

                    LogManager.Instance.LogInfo($"通过服务器 API 读取系统配置成功：Key={key}");
                    return result.Value ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"通过服务器 API 读取系统配置失败：Key={key}，错误={ex.Message}");
                return null;
            }
        }

        public async Task<bool?> SetAsync(string key, string value, CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl = ApiEndpoint.Build("api/system-config/" + Uri.EscapeDataString(key ?? string.Empty));
            try
            {
                string json = JsonConvert.SerializeObject(new SetRequest { Value = value ?? string.Empty });
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await HttpClient.PutAsync(requestUrl, content, cancellationToken).ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    LogManager.Instance.LogInfo($"系统配置写入接口响应：HTTP {(int)response.StatusCode} {response.ReasonPhrase}, BodyLength={responseBody.Length}, Key={key}");
                    MutationResponse result = JsonConvert.DeserializeObject<MutationResponse>(responseBody);
                    if (result == null || !response.IsSuccessStatusCode || !result.Success)
                        throw new InvalidOperationException(result?.Message ?? $"系统配置写入接口返回无效，HTTP {(int)response.StatusCode}。");

                    LogManager.Instance.LogInfo($"通过服务器 API 写入系统配置成功：Key={key}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"通过服务器 API 写入系统配置失败：Key={key}，错误={ex.Message}");
                return null;
            }
        }
    }
}

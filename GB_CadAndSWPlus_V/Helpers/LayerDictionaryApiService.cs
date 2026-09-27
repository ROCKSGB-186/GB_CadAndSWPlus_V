using GB_CadAndSWPlus_V.FunctionalMethod;
using GB_CadAndSWPlus_V.UniFiedStandards;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.Helpers
{
    /// <summary>
    /// 图层字典服务器接口客户端。
    /// 该类只通过 API 访问服务器，不直接创建 DM 连接。
    /// </summary>
    public sealed class LayerDictionaryApiService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = ApiEndpoint.RequestTimeout
        };

        private sealed class MutationResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public int AffectedRows { get; set; }
        }

        private sealed class SaveRequest
        {
            public string Username { get; set; } = string.Empty;
            public List<LayerDictionaryHelper> Entries { get; set; } = new List<LayerDictionaryHelper>();
        }

        private sealed class DeleteRequest
        {
            public List<int> Ids { get; set; } = new List<int>();
        }

        /// <summary>
        /// 请求服务器确保 LAYER_DICTIONARY 表存在。
        /// </summary>
        public async Task<bool> EnsureTableAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl = ApiEndpoint.Build("api/layer-dictionary/initialize");
            try
            {
                using (HttpResponseMessage response = await HttpClient
                    .PostAsync(requestUrl, content: null, cancellationToken)
                    .ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    MutationResponse result = JsonConvert.DeserializeObject<MutationResponse>(responseBody);
                    if (result == null)
                    {
                        throw new InvalidOperationException("图层字典初始化接口返回为空。");
                    }

                    if (!response.IsSuccessStatusCode || !result.Success)
                    {
                        throw new HttpRequestException(
                            $"图层字典初始化失败，HTTP {(int)response.StatusCode}，消息：{result.Message}");
                    }

                    LogManager.Instance.LogInfo($"通过服务器 API 初始化图层字典成功：地址={requestUrl}，消息={result.Message}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"通过服务器 API 初始化图层字典失败：地址={requestUrl}，错误={ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 查询个人图层字典；返回 null 表示 API 失败，空列表表示查询成功但没有记录。
        /// </summary>
        public async Task<List<LayerDictionaryHelper>> GetPersonalAsync(string username, CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl = ApiEndpoint.Build("api/layer-dictionary/personal?username=" + Uri.EscapeDataString(username ?? string.Empty));
            return await GetEntriesAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 查询标准图层字典；返回 null 表示 API 失败。
        /// </summary>
        public async Task<List<LayerDictionaryHelper>> GetStandardAsync(string major, CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl = ApiEndpoint.Build("api/layer-dictionary/standard?major=" + Uri.EscapeDataString(major ?? string.Empty));
            return await GetEntriesAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 通过服务器覆盖保存指定用户的图层字典。
        /// </summary>
        public async Task<bool> SavePersonalAsync(string username, List<LayerDictionaryHelper> entries, CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl = ApiEndpoint.Build("api/layer-dictionary/personal");
            try
            {
                string json = JsonConvert.SerializeObject(new SaveRequest
                {
                    Username = username ?? string.Empty,
                    Entries = entries ?? new List<LayerDictionaryHelper>()
                });
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await HttpClient.PostAsync(requestUrl, content, cancellationToken).ConfigureAwait(false))
                {
                    MutationResponse result = JsonConvert.DeserializeObject<MutationResponse>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (result == null || !response.IsSuccessStatusCode || !result.Success)
                        throw new InvalidOperationException(result?.Message ?? "保存图层字典接口返回为空。");

                    LogManager.Instance.LogInfo($"通过服务器 API 保存图层字典成功：用户名={username}，记录数={entries?.Count ?? 0}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"通过服务器 API 保存图层字典失败：用户名={username}，错误={ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 通过服务器按 ID 批量删除图层字典；返回 -1 表示 API 失败。
        /// </summary>
        public async Task<int> DeleteAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default(CancellationToken))
        {
            string requestUrl = ApiEndpoint.Build("api/layer-dictionary/entries");
            int[] idArray = (ids ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().ToArray();
            try
            {
                string json = JsonConvert.SerializeObject(new DeleteRequest { Ids = idArray.ToList() });
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, requestUrl) { Content = content })
                using (HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    MutationResponse result = JsonConvert.DeserializeObject<MutationResponse>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (result == null || !response.IsSuccessStatusCode || !result.Success)
                        throw new InvalidOperationException(result?.Message ?? "删除图层字典接口返回为空。");

                    LogManager.Instance.LogInfo($"通过服务器 API 删除图层字典成功：请求数={idArray.Length}，影响行数={result.AffectedRows}");
                    return result.AffectedRows;
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"通过服务器 API 删除图层字典失败：错误={ex.Message}");
                return -1;
            }
        }

        private static async Task<List<LayerDictionaryHelper>> GetEntriesAsync(string requestUrl, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpResponseMessage response = await HttpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{responseBody}");

                    List<LayerDictionaryHelper> result = JsonConvert.DeserializeObject<List<LayerDictionaryHelper>>(responseBody);
                    if (result == null)
                        throw new InvalidOperationException("图层字典查询接口返回为空。");

                    return result;
                }
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"通过服务器 API 查询图层字典失败：地址={requestUrl}，错误={ex.Message}");
                return null;
            }
        }
    }
}

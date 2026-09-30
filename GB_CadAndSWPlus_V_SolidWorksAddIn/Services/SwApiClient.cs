using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using System;
using System.Net.Http;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    public sealed class SwApiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        public SwApiClient(SwApiSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<byte[]> GetBytesAsync(
            string relativePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string url = BuildUrl(relativePath);
            using (HttpResponseMessage response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                string responseText = response.IsSuccessStatusCode
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"API 文件下载失败：HTTP {(int)response.StatusCode}，地址={url}，消息={responseText}");

                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        public SwApiSettings Settings { get; }

        public async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
            string relativePath,
            TRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string url = BuildUrl(relativePath);
            string requestJson = Serialize(request);
            using (var content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"API 请求失败：HTTP {(int)response.StatusCode}，地址={url}，消息={responseJson}");

                return Deserialize<TResponse>(responseJson);
            }
        }

        public async Task<TResponse> GetJsonAsync<TResponse>(
            string relativePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            string url = BuildUrl(relativePath);
            using (HttpResponseMessage response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false))
            {
                string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"API 请求失败：HTTP {(int)response.StatusCode}，地址={url}，消息={responseJson}");

                return Deserialize<TResponse>(responseJson);
            }
        }

        private string BuildUrl(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("API 路径不能为空。", nameof(relativePath));

            return new Uri(new Uri(Settings.GetBaseUrl() + "/"), relativePath.TrimStart('/')).ToString();
        }

        private static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("服务器返回了空响应。");

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                object? value = serializer.ReadObject(stream);
                if (value is T result)
                    return result;
            }

            throw new InvalidOperationException("服务器响应格式无法识别。");
        }
    }
}

using GB_CadAndSWPlus_V.FunctionalMethod;
using GB_CadAndSWPlus_V.UniFiedStandards;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.Helpers
{
    public sealed class GraphicApiService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = ApiEndpoint.RequestTimeout
        };

        public async Task<List<FileStorage>> GetFilesByCategoryIdAsync(
            int categoryId,
            string categoryType,
            CancellationToken cancellationToken = default)
        {
            if (categoryId <= 0)
                throw new ArgumentOutOfRangeException(nameof(categoryId));

            string type;
            if (string.Equals(categoryType, "main", StringComparison.OrdinalIgnoreCase))
            {
                type = "main";
            }
            else if (string.Equals(categoryType, "sub", StringComparison.OrdinalIgnoreCase))
            {
                type = "sub";
            }
            else
            {
                throw new ArgumentException("categoryType 必须是 main 或 sub。", nameof(categoryType));
            }
            string requestUrl = ApiEndpoint.Build($"api/graphics/category/{categoryId}?categoryType={type}");

            using (HttpResponseMessage response = await HttpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
            {
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"文件接口请求失败，HTTP {(int)response.StatusCode}，响应：{responseBody}");

                GraphicListApiResponse? result = JsonConvert.DeserializeObject<GraphicListApiResponse>(responseBody);
                if (result == null || !result.Success)
                    throw new InvalidOperationException(result?.Message ?? "服务器文件查询失败。");

                result.Files ??= new List<GraphicApiDto>();
                var files = new List<FileStorage>(result.Files.Count);
                foreach (GraphicApiDto item in result.Files)
                {
                    files.Add(new FileStorage
                    {
                        Id = item.Id,
                        CategoryId = item.CategoryId,
                        CategoryType = item.CategoryType,
                        FileAttributeId = item.FileAttributeId,
                        FileName = item.FileName,
                        FileStoredName = item.FileStoredName,
                        DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.FileName : item.DisplayName,
                        FileType = item.FileType,
                        FileHash = item.FileHash,
                        BlockName = item.BlockName,
                        LayerName = item.LayerName,
                        ColorIndex = item.ColorIndex,
                        Scale = item.Scale,
                        FilePath = item.FilePath,
                        PreviewImageName = item.PreviewImageName,
                        PreviewImagePath = item.PreviewImagePath,
                        FileSize = item.FileSize,
                        IsPreview = item.IsPreview,
                        Version = item.Version,
                        Description = item.Description,
                        IsActive = item.IsActive,
                        CreatedBy = item.CreatedBy,
                        Title = item.Title,
                        Keywords = item.Keywords,
                        IsPublic = item.IsPublic,
                        UpdatedBy = item.UpdatedBy,
                        LastAccessedAt = item.LastAccessedAt,
                        CreatedAt = item.CreatedAt ?? DateTime.MinValue,
                        UpdatedAt = item.UpdatedAt ?? DateTime.MinValue
                    });
                }

                LogManager.Instance.LogInfo($"通过服务器加载文件成功：地址={requestUrl}，文件数={files.Count}");
                return files;
            }
        }

        public async Task<List<FileStorage>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            string requestUrl = ApiEndpoint.Build("api/graphics/active");
            using (HttpResponseMessage response = await HttpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
            {
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"文件接口请求失败，HTTP {(int)response.StatusCode}，响应：{responseBody}");

                GraphicListApiResponse? result = JsonConvert.DeserializeObject<GraphicListApiResponse>(responseBody);
                if (result == null || !result.Success)
                    throw new InvalidOperationException(result?.Message ?? "服务器文件查询失败。");

                result.Files ??= new List<GraphicApiDto>();
                return result.Files.ConvertAll(ToFileStorage);
            }
        }

        public async Task<GraphicDetails?> GetDetailsByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id));

            string requestUrl = ApiEndpoint.Build($"api/graphics/{id}/details");
            return await GetDetailsAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateDetailsAsync(
            FileStorage storage,
            Dictionary<string, string> attributes,
            string? configName = null,
            CancellationToken cancellationToken = default)
        {
            if (storage == null || storage.Id <= 0)
                throw new ArgumentException("图元记录无效。", nameof(storage));

            string requestUrl = ApiEndpoint.Build($"api/graphics/{storage.Id}/details");
            var request = new
            {
                storage.FileName,
                storage.DisplayName,
                storage.BlockName,
                storage.LayerName,
                storage.ColorIndex,
                storage.Scale,
                storage.Title,
                storage.Keywords,
                storage.Description,
                UpdatedBy = storage.UpdatedBy,
                ConfigName = string.IsNullOrWhiteSpace(configName) ? storage.FileAttributeId : configName,
                Attributes = attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            using (var content = new StringContent(
                JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await HttpClient
                .PutAsync(requestUrl, content, cancellationToken).ConfigureAwait(false))
            {
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"图元属性更新失败，HTTP {(int)response.StatusCode}，响应：{responseBody}");
            }
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id));

            string requestUrl = ApiEndpoint.Build($"api/graphics/{id}");
            using (HttpResponseMessage response = await HttpClient
                .DeleteAsync(requestUrl, cancellationToken).ConfigureAwait(false))
            {
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"图元删除失败，HTTP {(int)response.StatusCode}，响应：{responseBody}");
            }
        }

        public async Task<GraphicDetails?> GetDetailsByHashAsync(
            string fileHash,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileHash))
                throw new ArgumentException("文件哈希不能为空。", nameof(fileHash));

            string requestUrl = ApiEndpoint.Build(
                $"api/graphics/hash/{Uri.EscapeDataString(fileHash.Trim())}/details");
            return await GetDetailsAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<GraphicDetails?> GetDetailsAsync(
            string requestUrl,
            CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await HttpClient
                .GetAsync(requestUrl, cancellationToken).ConfigureAwait(false))
            {
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"图元详情请求失败，HTTP {(int)response.StatusCode}，响应：{responseBody}");

                GraphicDetailsApiResponse? result = JsonConvert.DeserializeObject<GraphicDetailsApiResponse>(responseBody);
                if (result == null || !result.Success || result.File == null)
                    throw new InvalidOperationException(result?.Message ?? "服务器图元详情查询失败。");

                return new GraphicDetails
                {
                    File = ToFileStorage(result.File),
                    Attributes = result.Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ConfigName = result.ConfigName ?? string.Empty
                };
            }
        }

        private static FileStorage ToFileStorage(GraphicApiDto item)
        {
            return new FileStorage
            {
                Id = item.Id,
                CategoryId = item.CategoryId,
                CategoryType = item.CategoryType,
                FileAttributeId = item.FileAttributeId,
                FileName = item.FileName,
                FileStoredName = item.FileStoredName,
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.FileName : item.DisplayName,
                FileType = item.FileType,
                FileHash = item.FileHash,
                BlockName = item.BlockName,
                LayerName = item.LayerName,
                ColorIndex = item.ColorIndex,
                Scale = item.Scale,
                FilePath = item.FilePath,
                PreviewImageName = item.PreviewImageName,
                PreviewImagePath = item.PreviewImagePath,
                FileSize = item.FileSize,
                IsPreview = item.IsPreview,
                Version = item.Version,
                Description = item.Description,
                IsActive = item.IsActive,
                CreatedBy = item.CreatedBy,
                Title = item.Title,
                Keywords = item.Keywords,
                IsPublic = item.IsPublic,
                UpdatedBy = item.UpdatedBy,
                LastAccessedAt = item.LastAccessedAt,
                CreatedAt = item.CreatedAt ?? DateTime.MinValue,
                UpdatedAt = item.UpdatedAt ?? DateTime.MinValue
            };
        }

        private sealed class GraphicListApiResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public List<GraphicApiDto> Files { get; set; } = new List<GraphicApiDto>();
        }

        public sealed class GraphicDetails
        {
            public FileStorage File { get; set; } = new FileStorage();
            public Dictionary<string, string> Attributes { get; set; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string ConfigName { get; set; } = string.Empty;
        }

        private sealed class GraphicDetailsApiResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public GraphicApiDto? File { get; set; }
            public Dictionary<string, string>? Attributes { get; set; }
            public string? ConfigName { get; set; }
        }

        private sealed class GraphicApiDto
        {
            public int Id { get; set; }
            public int CategoryId { get; set; }
            public string? CategoryType { get; set; }
            public string? FileAttributeId { get; set; }
            public string? FileName { get; set; }
            public string? FileStoredName { get; set; }
            public string? DisplayName { get; set; }
            public string? FileType { get; set; }
            public string? FileHash { get; set; }
            public string? BlockName { get; set; }
            public string? LayerName { get; set; }
            public int? ColorIndex { get; set; }
            public double? Scale { get; set; }
            public string? FilePath { get; set; }
            public string? PreviewImageName { get; set; }
            public string? PreviewImagePath { get; set; }
            public long? FileSize { get; set; }
            public int IsPreview { get; set; }
            public int Version { get; set; }
            public string? Description { get; set; }
            public int IsActive { get; set; }
            public string? CreatedBy { get; set; }
            public string? Title { get; set; }
            public string? Keywords { get; set; }
            public int IsPublic { get; set; }
            public string? UpdatedBy { get; set; }
            public DateTime? LastAccessedAt { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
        }
    }
}

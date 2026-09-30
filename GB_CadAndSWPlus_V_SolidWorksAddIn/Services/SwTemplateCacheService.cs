using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 模板获取服务：服务器优先，本地缓存和 Resources 离线回退。
    /// </summary>
    public sealed class SwTemplateCacheService
    {
        private readonly SwApiClient _apiClient;
        private readonly string _cacheDirectory;

        public SwTemplateCacheService(SwApiClient apiClient)
        {
            // 第一步：保存 API 客户端，并建立当前用户专属的本地模板缓存目录。
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GB_CADPLUS",
                "SolidWorksAddIn",
                "Templates");
        }

        public string CacheDirectory => _cacheDirectory;

        public async Task<string?> GetTemplateAsync(
            string templateKey,
            string? serverDownloadPath,
            string? bundledResourcePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(templateKey))
                throw new ArgumentException("模板标识不能为空。", nameof(templateKey));

            // 第二步：根据模板代码和文件扩展名确定缓存文件路径。
            string safeKey = MakeSafeFileName(templateKey);
            string extension = GetExtension(serverDownloadPath, bundledResourcePath);
            string cachePath = Path.Combine(_cacheDirectory, safeKey + extension);

            if (!string.IsNullOrWhiteSpace(serverDownloadPath))
            {
                try
                {
                    // 第三步：优先从服务器下载最新模板，并覆盖本地缓存。
                    byte[] content = await _apiClient.GetBytesAsync(serverDownloadPath, cancellationToken).ConfigureAwait(false);
                    if (content.Length > 0)
                    {
                        Directory.CreateDirectory(_cacheDirectory);
                        File.WriteAllBytes(cachePath, content);
                        return cachePath;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Net.Http.HttpRequestException)
                {
                    // 第四步：服务器或网络不可用时，保留异常上下文并继续离线回退。
                    _ = ex;
                }
            }

            // 第五步：服务器下载失败时，优先使用已缓存的模板。
            if (File.Exists(cachePath))
                return cachePath;

            if (!string.IsNullOrWhiteSpace(bundledResourcePath))
            {
                // 第六步：没有缓存时，最后尝试项目 Resources 或安装目录中的内置模板。
                string resourcePath = Path.GetFullPath(bundledResourcePath);
                if (File.Exists(resourcePath))
                    return resourcePath;
            }

            // 第七步：所有来源均不存在时返回空，由装配体工作流创建空装配体或提示用户。
            return null;
        }

        private static string GetExtension(string? serverPath, string? resourcePath)
        {
            string extension = Path.GetExtension(serverPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(extension))
                extension = Path.GetExtension(resourcePath ?? string.Empty);
            return string.IsNullOrWhiteSpace(extension) ? ".sldasm" : extension.ToLowerInvariant();
        }

        private static string MakeSafeFileName(string value)
        {
            string result = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(result) ? "platform-template" : result;
        }
    }
}

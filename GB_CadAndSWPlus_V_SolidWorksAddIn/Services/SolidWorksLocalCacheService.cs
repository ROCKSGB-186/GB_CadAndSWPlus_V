using System;
using System.IO;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>SolidWorks 模型、预览图片和三维预览附件的本地缓存服务。</summary>
    public sealed class SolidWorksLocalCacheService
    {
        private readonly string _rootDirectory;

        public SolidWorksLocalCacheService()
        {
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _rootDirectory = Path.Combine(localApplicationData, "GB_CADPLUS", "SolidWorksCache");
        }

        /// <summary>获取模型缓存路径；模型文件名优先使用服务端返回的 ORIGINAL_FILE_NAME。</summary>
        public string GetModelPath(SolidWorksResourceApiService.ResourceDto resource, string extension)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            string safeExtension = NormalizeExtension(extension, ".sldprt");
            string originalFileName = Path.GetFileName(resource.OriginalFileName ?? string.Empty).Trim();
            string safeFileName = MakeSafeFileName(originalFileName, string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName))
                safeFileName = resource.Id.ToString();
            if (string.IsNullOrWhiteSpace(Path.GetExtension(safeFileName)))
                safeFileName += safeExtension;
            return Path.Combine(_rootDirectory, "Models", safeFileName);
        }

        /// <summary>获取指定预览附件的缓存路径。</summary>
        public string GetAttachmentPath(long resourceId, SolidWorksResourceApiService.AttachmentDto attachment)
        {
            if (attachment == null) throw new ArgumentNullException(nameof(attachment));
            string extension = Path.GetExtension(attachment.OriginalFileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".bin";
            string type = MakeSafeFileName(attachment.AttachmentType, "ATTACHMENT");
            return Path.Combine(_rootDirectory, "Attachments", resourceId + "_" + type + "_" + attachment.Id + "_v" + attachment.Version + NormalizeExtension(extension, ".bin"));
        }

        /// <summary>读取有效的模型缓存；缓存不存在或为空时返回 null。</summary>
        public string TryGetModel(SolidWorksResourceApiService.ResourceDto resource, string extension)
        {
            return TryGetExistingFile(GetModelPath(resource, extension), "模型", resource.Id);
        }

        /// <summary>读取有效的附件缓存；缓存不存在或为空时返回 null。</summary>
        public string TryGetAttachment(long resourceId, SolidWorksResourceApiService.AttachmentDto attachment)
        {
            return TryGetExistingFile(GetAttachmentPath(resourceId, attachment), attachment.AttachmentType, attachment.Id);
        }

        /// <summary>以临时文件方式写入缓存，避免中断下载留下损坏文件。</summary>
        public async Task<string> WriteAsync(string targetPath, byte[] bytes, string description)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("缓存路径不能为空。", nameof(targetPath));
            if (bytes == null || bytes.Length == 0) throw new InvalidOperationException(description + "内容为空，未写入缓存。");

            string directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("无法确定缓存目录。");
            Directory.CreateDirectory(directory);
            string temporaryPath = targetPath + ".download";
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(temporaryPath, targetPath);
                SolidWorksFileLogger.Info("SolidWorks 本地缓存写入成功；类型=" + description + "；大小=" + bytes.Length + "字节。" );
                return targetPath;
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private string TryGetExistingFile(string path, string description, long id)
        {
            if (File.Exists(path))
            {
                FileInfo info = new FileInfo(path);
                if (info.Length > 0)
                {
                    SolidWorksFileLogger.Info("SolidWorks 本地缓存命中；类型=" + description + "；标识=" + id + "；大小=" + info.Length + "字节。" );
                    return path;
                }
                SolidWorksFileLogger.Warning("SolidWorks 本地缓存无效，将重新下载；类型=" + description + "；标识=" + id + "。" );
                File.Delete(path);
            }
            return null;
        }

        private static string NormalizeExtension(string extension, string fallback)
        {
            string value = (extension ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            return value.StartsWith(".", StringComparison.Ordinal) ? value.ToLowerInvariant() : "." + value.ToLowerInvariant();
        }

        private static string MakeSafeFileName(string value, string fallback)
        {
            string text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) text = text.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
    }
}

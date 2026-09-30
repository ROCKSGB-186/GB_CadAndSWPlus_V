using GB_CadAndSWPlus_V.SolidWorksAddIn.Models;
using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// SolidWorks 图元下载、缓存、插入和实例注册闭环服务。
    /// </summary>
    public sealed class SwGraphicInsertService
    {
        private readonly ISldWorks _solidWorks;
        private readonly SwGraphicService _graphicService;
        private readonly SwInstanceClientService _instanceClient;
        private readonly string _cacheDirectory;

        public SwGraphicInsertService(
            ISldWorks solidWorks,
            SwGraphicService graphicService,
            SwInstanceClientService instanceClient)
        {
            _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
            _graphicService = graphicService ?? throw new ArgumentNullException(nameof(graphicService));
            _instanceClient = instanceClient ?? throw new ArgumentNullException(nameof(instanceClient));
            _cacheDirectory = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "GB_CADPLUS", "SolidWorksAddIn", "Graphics");
        }

        /// <summary>
        /// 下载图元并插入当前活动装配体，然后注册服务器实例。
        /// </summary>
        public async Task<SwGraphicInsertResult> InsertAsync(
            int graphicId,
            string? localFallbackPath = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // 第一步：确认当前文档是装配体，避免把组件插入零件或空文档。
            ModelDoc2? activeDocument = _solidWorks.IActiveDoc2;
            if (activeDocument == null || activeDocument.GetType() != (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocASSEMBLY)
                throw new InvalidOperationException("请先在 SolidWorks 中打开或激活装配体文档。");

            // 第二步：查询图元元数据，并按服务器优先、本地缓存、内置文件顺序取得文件。
            SwGraphicDetailsResponse details = await _graphicService
                .GetDetailsAsync(graphicId, cancellationToken).ConfigureAwait(false);
            if (!details.Success || details.File == null)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details.Message) ? "服务器未返回有效图元。" : details.Message);

            string? componentPath = await GetCachedFileAsync(details.File, localFallbackPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(componentPath) || !File.Exists(componentPath))
                throw new FileNotFoundException("找不到可插入的 SolidWorks 图元文件。", componentPath);

            // 第三步：通过 SolidWorks 装配体 API 在原点插入组件。
            IAssemblyDoc assembly = (IAssemblyDoc)activeDocument;
            Component2? component = assembly.AddComponent5(
                componentPath,
                0,
                string.Empty,
                false,
                string.Empty,
                0,
                0,
                0);
            if (component == null)
                throw new InvalidOperationException("SolidWorks 未能插入图元组件。");

            string swInstanceId = Guid.NewGuid().ToString("N");
            string pidInstanceId = CreatePidInstanceId(details.File, activeDocument.GetTitle(), component.Name2);
            var customProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["GraphicId"] = details.File.Id.ToString(),
                ["FileHash"] = details.File.FileHash ?? string.Empty,
                ["Source"] = "SolidWorksAddIn",
                ["InsertPoint"] = "0,0,0"
            };

            // 第四步：把插入实例登记到统一服务器，数据库版本和幂等规则由服务器处理。
            SwInstanceResponse registration = await _instanceClient.RegisterAsync(
                new SwInstanceRegisterRequest
                {
                    SwInstanceId = swInstanceId,
                    PidInstanceId = pidInstanceId,
                    LibraryItemId = details.File.Id,
                    DocumentPath = activeDocument.GetPathName(),
                    DocumentName = activeDocument.GetTitle(),
                    AssemblyName = activeDocument.GetTitle(),
                    ComponentName = component.Name2,
                    ConfigurationName = string.Empty,
                    TransformJson = "{\"x\":0,\"y\":0,\"z\":0}",
                    CustomProperties = customProperties,
                    Version = 1,
                    HasExpectedVersion = false
                }, cancellationToken).ConfigureAwait(false);

            return new SwGraphicInsertResult
            {
                GraphicId = details.File.Id,
                FilePath = componentPath,
                SwInstanceId = swInstanceId,
                PidInstanceId = pidInstanceId,
                ComponentName = component.Name2,
                RegistrationSucceeded = registration.Success,
                RegistrationMessage = registration.Message
            };
        }

        private async Task<string?> GetCachedFileAsync(
            SwGraphicDto graphic,
            string? localFallbackPath,
            CancellationToken cancellationToken)
        {
            // 第一步：使用图元 ID、版本和扩展名构造稳定缓存文件名。
            string extension = Path.GetExtension(graphic.FileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".sldprt";
            string cachePath = Path.Combine(_cacheDirectory, "graphic_" + graphic.Id + "_v" + graphic.Version + extension.ToLowerInvariant());

            // 第二步：服务器下载成功后覆盖缓存，确保优先使用最新版本。
            try
            {
                byte[] content = await _graphicService.DownloadFileAsync(graphic.Id, cancellationToken).ConfigureAwait(false);
                if (content.Length > 0)
                {
                    Directory.CreateDirectory(_cacheDirectory);
                    File.WriteAllBytes(cachePath, content);
                    return cachePath;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Net.Http.HttpRequestException)
            {
                // 网络或权限异常时继续走缓存和本地资源回退，不能阻断离线功能。
                _ = ex;
            }

            if (File.Exists(cachePath))
                return cachePath;
            if (!string.IsNullOrWhiteSpace(localFallbackPath) && File.Exists(localFallbackPath))
                return Path.GetFullPath(localFallbackPath);
            return null;
        }

        private static string CreatePidInstanceId(SwGraphicDto graphic, string documentName, string componentName)
        {
            string source = (graphic.FileHash ?? graphic.Id.ToString()) + "|" + documentName + "|" + componentName;
            return Guid.NewGuid().ToString("N") + "-" + source.GetHashCode().ToString("X8");
        }
    }

    /// <summary>
    /// 图元插入和服务器登记结果。
    /// </summary>
    public sealed class SwGraphicInsertResult
    {
        public int GraphicId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string SwInstanceId { get; set; } = string.Empty;
        public string PidInstanceId { get; set; } = string.Empty;
        public string ComponentName { get; set; } = string.Empty;
        public bool RegistrationSucceeded { get; set; }
        public string RegistrationMessage { get; set; } = string.Empty;
    }
}

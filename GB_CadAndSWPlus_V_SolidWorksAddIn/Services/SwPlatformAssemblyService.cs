using GB_CadAndSWPlus_V.SolidWorksAddIn.Configuration;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GB_CadAndSWPlus_V.SolidWorksAddIn.Services
{
    /// <summary>
    /// 平台专用 SolidWorks 装配体工作流。
    /// </summary>
    public sealed class SwPlatformAssemblyService
    {
        private readonly ISldWorks _solidWorks;
        private readonly SwTemplateCacheService _templateCache;

        public SwPlatformAssemblyService(ISldWorks solidWorks, SwTemplateCacheService templateCache)
        {
            // 第一步：保存 SolidWorks 应用对象和模板缓存服务，后续所有模型操作都通过当前连接执行。
            _solidWorks = solidWorks ?? throw new ArgumentNullException(nameof(solidWorks));
            _templateCache = templateCache ?? throw new ArgumentNullException(nameof(templateCache));
        }

        public async Task<string?> OpenOrCreateAsync(
            string platformCode,
            string? serverDownloadPath,
            string? bundledResourcePath,
            string outputDirectory,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(platformCode))
                throw new ArgumentException("平台代码不能为空。", nameof(platformCode));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("装配体输出目录不能为空。", nameof(outputDirectory));

            // 第二步：按照服务器优先、缓存其次、Resources 最后的顺序获取模板。
            string? templatePath = await _templateCache
                .GetTemplateAsync(platformCode, serverDownloadPath, bundledResourcePath, cancellationToken)
                .ConfigureAwait(false);

            string assemblyPath = Path.Combine(outputDirectory, MakeSafeFileName(platformCode) + ".sldasm");
            Directory.CreateDirectory(outputDirectory);

            int errors = 0;
            int warnings = 0;
            ModelDoc2? document = null;
            if (File.Exists(assemblyPath))
            {
                // 第三步：如果用户之前已经创建过平台装配体，则直接打开已有文件。
                document = _solidWorks.OpenDoc6(
                    assemblyPath,
                    (int)swDocumentTypes_e.swDocASSEMBLY,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    string.Empty,
                    ref errors,
                    ref warnings);
            }
            else if (!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))
            {
                // 第四步：打开模板文件，并另存为当前平台专用装配体。
                document = _solidWorks.OpenDoc6(
                    templatePath,
                    (int)swDocumentTypes_e.swDocASSEMBLY,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    string.Empty,
                    ref errors,
                    ref warnings);
                if (document != null)
                    document.SaveAs3(assemblyPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
            }
            else
            {
                // 第五步：没有任何可用模板时创建空装配体，保证离线流程仍可继续。
                document = (ModelDoc2)_solidWorks.NewDocument(
                    string.Empty,
                    (int)swDocumentTypes_e.swDocASSEMBLY,
                    0,
                    0);
                if (document != null)
                    document.SaveAs3(assemblyPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
            }

            if (document == null)
                throw new InvalidOperationException($"无法打开或创建平台装配体：{platformCode}，错误码={errors}，警告码={warnings}。");

            // 第六步：返回装配体路径，供页面显示并供后续实例插入和同步使用。
            return assemblyPath;
        }

        private static string MakeSafeFileName(string value)
        {
            string result = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(result) ? "platform" : result;
        }
    }
}

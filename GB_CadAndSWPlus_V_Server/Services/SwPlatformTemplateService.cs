using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>
/// SolidWorks 平台模板查询服务。
/// 数据库和文件存储只由服务器访问，客户端只接收 API 下载路径。
/// </summary>
public sealed class SwPlatformTemplateService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SwPlatformTemplateService> _logger;

    /// <summary>
    /// 创建平台模板查询服务。
    /// </summary>
    public SwPlatformTemplateService(
        IConfiguration configuration,
        ILogger<SwPlatformTemplateService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 按平台代码查询已配置的 SolidWorks 模板。
    /// </summary>
    public async Task<SwPlatformTemplateResponse> GetAsync(
        string platformCode,
        CancellationToken cancellationToken = default)
    {
        // 第一步：规范化平台代码，并校验配置中的 FileId。
        string normalizedCode = NormalizePlatformCode(platformCode);
        string configurationKey = $"SolidWorks:PlatformTemplates:{normalizedCode}:FileId";
        long? fileId = _configuration.GetValue<long?>(configurationKey);
        if (!fileId.HasValue || fileId.Value <= 0)
        {
            _logger.LogWarning("SolidWorks 平台模板未配置。PlatformCode={PlatformCode}, ConfigurationKey={ConfigurationKey}", normalizedCode, configurationKey);
            return new SwPlatformTemplateResponse
            {
                Success = false,
                Message = "服务器尚未配置该平台的 SolidWorks 模板，客户端将尝试本地缓存或 Resources。",
                PlatformCode = normalizedCode
            };
        }

        // 第二步：按服务器数据库配置读取统一附件表元数据。
        PlatformTemplateFileRow? file = await FindFileAsync(fileId.Value, cancellationToken).ConfigureAwait(false);
        if (file == null)
        {
            _logger.LogWarning("SolidWorks 平台模板附件不存在或已删除。PlatformCode={PlatformCode}, FileId={FileId}", normalizedCode, fileId.Value);
            return new SwPlatformTemplateResponse
            {
                Success = false,
                Message = "服务器模板附件不存在或已删除，客户端将尝试本地回退。",
                PlatformCode = normalizedCode,
                FileId = fileId.Value
            };
        }

        // 第三步：阻止普通 PDF、Excel 等附件被错误绑定为 SolidWorks 模板。
        string extension = Path.GetExtension(file.FileName ?? string.Empty);
        if (!IsSolidWorksExtension(extension))
        {
            _logger.LogError("SolidWorks 平台模板附件扩展名不合法。PlatformCode={PlatformCode}, FileId={FileId}, FileName={FileName}, Extension={Extension}", normalizedCode, fileId.Value, file.FileName, extension);
            return CreateInvalidResponse(normalizedCode, fileId.Value, file, "配置的附件不是 SolidWorks 模板（要求 .sldasm 或 .sldprt），客户端将尝试本地回退。");
        }

        // 第四步：阻止空附件进入客户端缓存。
        if (file.FileSize <= 0)
        {
            _logger.LogError("SolidWorks 平台模板附件大小无效。PlatformCode={PlatformCode}, FileId={FileId}, FileName={FileName}, FileSize={FileSize}", normalizedCode, fileId.Value, file.FileName, file.FileSize);
            return CreateInvalidResponse(normalizedCode, fileId.Value, file, "配置的 SolidWorks 模板附件为空，客户端将尝试本地回退。");
        }

        // 第五步：只返回下载 API 路径，不暴露服务器物理文件路径。
        string downloadPath = $"api/standards/management/files/{fileId.Value}/download";
        _logger.LogInformation("SolidWorks 平台模板查询成功。PlatformCode={PlatformCode}, FileId={FileId}, FileName={FileName}, FileSize={FileSize}, Version={Version}", normalizedCode, fileId.Value, file.FileName, file.FileSize, file.Version);
        return new SwPlatformTemplateResponse
        {
            Success = true,
            Message = "平台模板查询成功。",
            PlatformCode = normalizedCode,
            FileId = fileId.Value,
            FileName = file.FileName,
            FileSize = file.FileSize,
            Version = file.Version,
            DownloadPath = downloadPath
        };
    }

    /// <summary>
    /// 检查平台配置的附件是否可以作为 SolidWorks 模板使用。
    /// </summary>
    public async Task<SwPlatformTemplateBindingCheckResponse> CheckBindingAsync(
        string platformCode,
        CancellationToken cancellationToken = default)
    {
        // 第一步：复用客户端实际使用的查询逻辑，避免检查结果与下载结果不一致。
        SwPlatformTemplateResponse template = await GetAsync(platformCode, cancellationToken).ConfigureAwait(false);
        bool configured = template.FileId.HasValue && template.FileId.Value > 0;
        bool fileExists = configured && !string.IsNullOrWhiteSpace(template.FileName);
        bool isSolidWorksFile = IsSolidWorksExtension(Path.GetExtension(template.FileName ?? string.Empty));
        bool hasContent = template.FileSize > 0;

        // 第二步：返回管理员可读的绑定状态；未配置模板不是服务器故障。
        SwPlatformTemplateBindingCheckResponse result = new()
        {
            Success = true,
            Message = template.Success ? "平台模板绑定有效。" : template.Message,
            PlatformCode = template.PlatformCode,
            ConfiguredFileId = template.FileId,
            FileExists = fileExists,
            IsSolidWorksFile = isSolidWorksFile,
            HasContent = hasContent,
            FileName = template.FileName,
            FileSize = template.FileSize,
            Version = template.Version,
            DownloadPath = template.DownloadPath
        };
        _logger.LogInformation("SolidWorks 平台模板绑定检查完成。PlatformCode={PlatformCode}, FileId={FileId}, IsUsable={IsUsable}", result.PlatformCode, result.ConfiguredFileId, result.IsUsable);
        return result;
    }

    /// <summary>
    /// 查询统一规范附件表中的文件元数据。
    /// </summary>
    private async Task<PlatformTemplateFileRow?> FindFileAsync(
        long fileId,
        CancellationToken cancellationToken)
    {
        // 第一步：确定数据库类型、模式和附件表名称。
        string databaseType = (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
        string table = databaseType == "MYSQL" ? "standard_document_files" : $"{schema}.STANDARD_DOCUMENT_FILES";
        string parameterPrefix = databaseType == "MYSQL" ? "@" : ":";

        // 第二步：只读取客户端所需的附件元数据，不读取服务器物理路径。
        string sql = $"SELECT ORIGINAL_FILE_NAME AS FileName, COALESCE(SHA256, '') AS Version, FILE_SIZE AS FileSize FROM {table} WHERE ID={parameterPrefix}FileId AND IS_DELETED=0";
        await using DbConnection connection = databaseType == "MYSQL"
            ? new MySqlConnection(GetConnectionString(databaseType))
            : new DmConnection(GetConnectionString(databaseType));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<PlatformTemplateFileRow>(
            new CommandDefinition(sql, new { FileId = fileId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// 生成非法模板的统一响应。
    /// </summary>
    private static SwPlatformTemplateResponse CreateInvalidResponse(
        string platformCode,
        long fileId,
        PlatformTemplateFileRow file,
        string message)
    {
        return new SwPlatformTemplateResponse
        {
            Success = false,
            Message = message,
            PlatformCode = platformCode,
            FileId = fileId,
            FileName = file.FileName,
            FileSize = file.FileSize,
            Version = file.Version
        };
    }

    /// <summary>
    /// 判断文件扩展名是否为 SolidWorks 模型文件。
    /// </summary>
    private static bool IsSolidWorksExtension(string extension)
    {
        return string.Equals(extension, ".sldasm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".sldprt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 规范化平台代码。
    /// </summary>
    private static string NormalizePlatformCode(string platformCode)
    {
        string normalizedCode = (platformCode ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCode.Length == 0)
            throw new ArgumentException("平台代码不能为空。", nameof(platformCode));
        return normalizedCode;
    }

    /// <summary>
    /// 获取当前服务器配置的数据库连接字符串。
    /// </summary>
    private string GetConnectionString(string databaseType)
    {
        string connectionString = (_configuration["Database:ConnectionString"]
            ?? _configuration.GetConnectionString(databaseType == "MYSQL" ? "MySQL" : "DM")
            ?? string.Empty).Trim();
        if (connectionString.Length == 0)
            throw new InvalidOperationException("服务器数据库连接字符串未配置。");
        return connectionString;
    }

    /// <summary>
    /// 数据库附件元数据内部映射模型。
    /// </summary>
    private sealed class PlatformTemplateFileRow
    {
        public string FileName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public long FileSize { get; init; }
    }
}

using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>
/// 图元文件受控下载服务。
/// 客户端只能通过此服务读取文件，不能直接访问数据库或服务器物理路径。
/// </summary>
public sealed class GraphicFileDownloadService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphicFileDownloadService> _logger;

    /// <summary>
    /// 创建图元文件下载服务。
    /// </summary>
    public GraphicFileDownloadService(IConfiguration configuration, ILogger<GraphicFileDownloadService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 按图元 ID 打开实际文件流和下载元数据。
    /// </summary>
    public async Task<GraphicFileDownloadResult?> OpenAsync(int id, CancellationToken cancellationToken = default)
    {
        // 第一步：校验图元 ID，避免无效请求进入数据库。
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "图元 ID 必须大于 0。");

        // 第二步：查询有效图元的相对文件路径和原始文件名。
        GraphicFileRow? row = await FindFileAsync(id, cancellationToken).ConfigureAwait(false);
        if (row == null)
        {
            _logger.LogWarning("图元文件元数据不存在。图元编号={GraphicId}", id);
            return null;
        }

        // 第三步：只允许读取配置根目录下的相对路径，阻止路径穿越。
        string fullPath;
        try
        {
            fullPath = ResolveExistingPath(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "图元文件路径解析失败。图元编号={GraphicId}；数据库路径={StoredPath}；存储文件名={StoredFileName}", id, row.FilePath, row.FileStoredName);
            throw;
        }
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("图元物理文件不存在。图元编号={GraphicId}；数据库路径={StoredPath}；解析路径={ResolvedPath}", id, row.FilePath, fullPath);
            return null;
        }

        // 第四步：返回只读文件流，供 Controller 以 HTTP 文件响应输出。
        string fileName = string.IsNullOrWhiteSpace(row.FileName) ? $"graphic_{id}" : Path.GetFileName(row.FileName);
        string contentType = ResolveContentType(fileName);
        _logger.LogInformation("图元文件下载准备完成。图元编号={GraphicId}；文件名={FileName}；文件大小={FileSize}", id, fileName, new FileInfo(fullPath).Length);
        try
        {
            return new GraphicFileDownloadResult
            {
                Content = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read),
                FileName = fileName,
                ContentType = contentType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "图元文件打开失败。图元编号={GraphicId}；解析路径={ResolvedPath}", id, fullPath);
            throw;
        }
    }

    private async Task<GraphicFileRow?> FindFileAsync(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始查询图元文件数据库记录。图元编号={GraphicId}", id);
        // 第一步：按当前数据库类型生成兼容 SQL，并同时读取备用路径所需字段。
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "MYSQL" ? "cad_file_storage" : $"{schema}.CAD_FILE_STORAGE";
        string parameter = databaseType == "MYSQL" ? "@" : ":";
        string sql = $"SELECT file_name AS FileName, file_path AS FilePath, file_stored_name AS FileStoredName, category_type AS CategoryType, category_id AS CategoryId FROM {table} WHERE id={parameter}Id AND is_active=1";

        // 第二步：服务器使用统一数据库连接，客户端不参与数据库访问。
        await using DbConnection connection = databaseType == "MYSQL"
            ? new MySqlConnection(GetConnectionString("MYSQL"))
            : new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GraphicFileRow? row = await connection.QuerySingleOrDefaultAsync<GraphicFileRow>(
                new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            _logger.LogInformation("图元文件数据库查询完成。图元编号={GraphicId}；是否找到={Found}", id, row != null);
            return row;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "图元文件数据库查询失败。图元编号={GraphicId}；数据库类型={DatabaseType}", id, databaseType);
            throw;
        }
    }

    private string ResolveExistingPath(GraphicFileRow row)
    {
        string root = GetStorageRoot();
        string primaryPath = GetSafeFullPath(row.FilePath, root);
        if (File.Exists(primaryPath))
            return primaryPath;

        if (!string.IsNullOrWhiteSpace(row.FileStoredName)
            && row.CategoryId > 0
            && !string.IsNullOrWhiteSpace(row.CategoryType))
        {
            string fallbackPath = GetSafeFullPath(
                Path.Combine(row.CategoryType.Trim(), row.CategoryId.ToString(), row.FileStoredName.Trim()), root);
            _logger.LogWarning("数据库路径不存在，尝试按当前存储目录查找备用路径。数据库路径={StoredPath}；备用路径={FallbackPath}", row.FilePath, fallbackPath);
            if (File.Exists(fallbackPath))
                return fallbackPath;
        }

        _logger.LogWarning("图元文件候选路径均不存在。数据库路径={StoredPath}；存储根目录={Root}", row.FilePath, root);
        return primaryPath;
    }

    private string GetStorageRoot()
    {
        string root = (_configuration["Storage:Root"]
            ?? _configuration["Storage:StandardRoot"]
            ?? _configuration["StorageSettings:RootPath"]
            ?? _configuration["UploadStorage:RootPath"]
            ?? string.Empty).Trim();
        if (root.Length == 0)
            root = Path.Combine(AppContext.BaseDirectory, "StandardFiles");
        return Path.GetFullPath(root);
    }

    private string GetSafeFullPath(string? storedPath, string rootFullPath)
    {
        // 第一步：读取统一存储根目录。
        if (string.IsNullOrWhiteSpace(storedPath))
            throw new FileNotFoundException("图元文件路径为空。");

        // 上传历史记录保存过绝对路径，直接规范化后校验其仍在配置根目录内。
        string fullPath = Path.IsPathRooted(storedPath)
            ? Path.GetFullPath(storedPath)
            : Path.GetFullPath(Path.Combine(rootFullPath, storedPath));
        _logger.LogInformation("解析图元文件路径。数据库路径={StoredPath}；存储根目录={Root}；解析路径={ResolvedPath}", storedPath, rootFullPath, fullPath);
        string prefix = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!string.Equals(fullPath, rootFullPath, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("图元文件路径不合法。");
        return fullPath;
    }

    private static string ResolveContentType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".sldprt" => "application/octet-stream",
            ".sldasm" => "application/octet-stream",
            ".dwg" => "application/acad",
            _ => "application/octet-stream"
        };
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";

    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || schema.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Database:Schema 配置无效。");
        return schema.ToUpperInvariant();
    }

    private string GetConnectionString(string databaseType)
    {
        string connectionString = (_configuration["Database:ConnectionString"]
            ?? _configuration.GetConnectionString(databaseType == "MYSQL" ? "MySQL" : "DM")
            ?? string.Empty).Trim();
        if (connectionString.Length == 0)
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        return connectionString;
    }

    private sealed class GraphicFileRow
    {
        public string? FileName { get; init; }
        public string? FilePath { get; init; }
        public string? FileStoredName { get; init; }
        public string? CategoryType { get; init; }
        public int CategoryId { get; init; }
    }
}

/// <summary>
/// 图元文件下载结果。
/// </summary>
public sealed class GraphicFileDownloadResult
{
    public Stream Content { get; init; } = Stream.Null;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
}

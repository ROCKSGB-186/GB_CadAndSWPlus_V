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
            _logger.LogWarning("图元文件元数据不存在。GraphicId={GraphicId}", id);
            return null;
        }

        // 第三步：只允许读取配置根目录下的相对路径，阻止路径穿越。
        string fullPath = GetSafeFullPath(row.FilePath);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("图元物理文件不存在。GraphicId={GraphicId}, RelativePath={RelativePath}", id, row.FilePath);
            return null;
        }

        // 第四步：返回只读文件流，供 Controller 以 HTTP 文件响应输出。
        string fileName = string.IsNullOrWhiteSpace(row.FileName) ? $"graphic_{id}" : Path.GetFileName(row.FileName);
        string contentType = ResolveContentType(fileName);
        _logger.LogInformation("图元文件下载准备完成。GraphicId={GraphicId}, FileName={FileName}, FileSize={FileSize}", id, fileName, new FileInfo(fullPath).Length);
        return new GraphicFileDownloadResult
        {
            Content = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            FileName = fileName,
            ContentType = contentType
        };
    }

    private async Task<GraphicFileRow?> FindFileAsync(int id, CancellationToken cancellationToken)
    {
        // 第一步：按当前数据库类型生成兼容 SQL。
        string databaseType = GetDatabaseType();
        string schema = GetSchemaName();
        string table = databaseType == "MYSQL" ? "cad_file_storage" : $"{schema}.CAD_FILE_STORAGE";
        string parameter = databaseType == "MYSQL" ? "@" : ":";
        string sql = $"SELECT file_name AS FileName, file_path AS FilePath FROM {table} WHERE id={parameter}Id AND is_active=1";

        // 第二步：服务器使用统一数据库连接，客户端不参与数据库访问。
        await using DbConnection connection = databaseType == "MYSQL"
            ? new MySqlConnection(GetConnectionString("MYSQL"))
            : new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<GraphicFileRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private string GetSafeFullPath(string? relativePath)
    {
        // 第一步：兼容现有图元存储目录配置。
        string root = (_configuration["Storage:Root"]
            ?? _configuration["Storage:StandardRoot"]
            ?? _configuration["StorageSettings:RootPath"]
            ?? _configuration["UploadStorage:RootPath"]
            ?? string.Empty).Trim();
        if (root.Length == 0)
            root = Path.Combine(AppContext.BaseDirectory, "StandardFiles");

        string rootFullPath = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath ?? string.Empty));
        string prefix = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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

using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Text.Json;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

public sealed class GraphicDetailsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphicDetailsService> _logger;

    public GraphicDetailsService(IConfiguration configuration, ILogger<GraphicDetailsService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GraphicDetailsResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "图元 ID 必须大于 0。");

        return await QueryAsync("id", id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GraphicDetailsResponse?> GetByHashAsync(string fileHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
            throw new ArgumentException("文件哈希不能为空。", nameof(fileHash));

        return await QueryAsync("file_hash", fileHash.Trim(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<GraphicDetailsResponse?> QueryAsync(string key, object value, CancellationToken cancellationToken)
    {
        string type = GetDatabaseType();
        string schema = GetSchemaName();
        string prefix = type == "MYSQL" ? "@" : ":";
        string table = type == "MYSQL" ? "cad_file_storage" : $"{schema}.CAD_FILE_STORAGE";
        string column = key == "id" ? "id" : "file_hash";
        string sql = $@"
SELECT id AS Id, category_id AS CategoryId, category_type AS CategoryType,
       file_attribute_id AS FileAttributeId, file_name AS FileName,
       file_stored_name AS FileStoredName, display_name AS DisplayName,
       file_type AS FileType, file_hash AS FileHash, block_name AS BlockName,
       layer_name AS LayerName, color_index AS ColorIndex, scale AS Scale,
       file_path AS FilePath, preview_image_name AS PreviewImageName,
       preview_image_path AS PreviewImagePath, file_size AS FileSize,
       is_preview AS IsPreview, version AS Version, description AS Description,
       is_active AS IsActive, created_by AS CreatedBy, title AS Title,
       keywords AS Keywords, is_public AS IsPublic, updated_by AS UpdatedBy,
       last_accessed_at AS LastAccessedAt, created_at AS CreatedAt,
       updated_at AS UpdatedAt
FROM {table}
WHERE {column} = {prefix}Value
ORDER BY created_at DESC";
        sql += type == "MYSQL" ? " LIMIT 1" : " FETCH FIRST 1 ROWS ONLY";

        await using var connection = type == "MYSQL"
            ? (System.Data.Common.DbConnection)new MySqlConnection(GetConnectionString("MYSQL"))
            : new DmConnection(GetConnectionString("DM"));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        GraphicDto? file = await connection.QueryFirstOrDefaultAsync<GraphicDto>(
            new CommandDefinition(sql, new { Value = value }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (file == null)
            return null;

        string attrTable = type == "MYSQL" ? "cad_block_attributes_json" : $"{schema}.CAD_BLOCK_ATTRIBUTES_JSON";
        string attrSql = $@"SELECT config_name AS ConfigName, attributes_json AS AttributesJson
FROM {attrTable}
WHERE file_id = {prefix}FileId
ORDER BY attr_id DESC";
        attrSql += type == "MYSQL" ? " LIMIT 1" : " FETCH FIRST 1 ROWS ONLY";

        var attr = await connection.QueryFirstOrDefaultAsync<AttributeRow>(
            new CommandDefinition(attrSql, new { FileId = file.Id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        Dictionary<string, string> attributes = new(StringComparer.OrdinalIgnoreCase);
        if (attr != null && !string.IsNullOrWhiteSpace(attr.AttributesJson))
        {
            try
            {
                attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(attr.AttributesJson)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "图元属性 JSON 无法解析。FileId={FileId}", file.Id);
            }
        }

        return new GraphicDetailsResponse
        {
            Success = true,
            Message = "图元详情查询成功",
            File = file,
            Attributes = attributes,
            ConfigName = attr?.ConfigName ?? string.Empty
        };
    }

    private sealed class AttributeRow
    {
        public string? ConfigName { get; set; }
        public string? AttributesJson { get; set; }
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
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = (_configuration.GetConnectionString(databaseType == "MYSQL" ? "MySQL" : "DM") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"缺少 {databaseType} 数据库连接字符串配置。");
        return connectionString;
    }
}

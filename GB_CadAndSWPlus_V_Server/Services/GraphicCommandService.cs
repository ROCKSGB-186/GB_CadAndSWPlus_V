using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Text.Json;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

public sealed class GraphicCommandService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphicCommandService> _logger;

    public GraphicCommandService(IConfiguration configuration, ILogger<GraphicCommandService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> UpdateDetailsAsync(
        int id,
        GraphicMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "图元 ID 必须大于 0。");

        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string storageTable = GetTableName(type, "CAD_FILE_STORAGE");
        string attributeTable = GetTableName(type, "CAD_BLOCK_ATTRIBUTES_JSON");
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            string updateStorageSql = $@"
UPDATE {storageTable}
SET file_name = {prefix}FileName,
    display_name = {prefix}DisplayName,
    block_name = {prefix}BlockName,
    layer_name = {prefix}LayerName,
    color_index = {prefix}ColorIndex,
    scale = {prefix}Scale,
    title = {prefix}Title,
    keywords = {prefix}Keywords,
    description = {prefix}Description,
    updated_by = {prefix}UpdatedBy,
    updated_at = {prefix}UpdatedAt
WHERE id = {prefix}Id";
            int updatedRows = await connection.ExecuteAsync(new CommandDefinition(
                updateStorageSql,
                new
                {
                    Id = id,
                    request.FileName,
                    request.DisplayName,
                    request.BlockName,
                    request.LayerName,
                    request.ColorIndex,
                    request.Scale,
                    request.Title,
                    request.Keywords,
                    request.Description,
                    request.UpdatedBy,
                    UpdatedAt = DateTime.Now
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (updatedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            string configName = string.IsNullOrWhiteSpace(request.ConfigName)
                ? "default"
                : request.ConfigName.Trim();
            string attributesJson = JsonSerializer.Serialize(request.Attributes
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            DateTime now = DateTime.Now;

            string updateAttributesSql = $@"
UPDATE {attributeTable}
SET attributes_json = {prefix}AttributesJson,
    updated_at = {prefix}UpdatedAt
WHERE file_id = {prefix}FileId
  AND config_name = {prefix}ConfigName";
            int updatedAttributeRows = await connection.ExecuteAsync(new CommandDefinition(
                updateAttributesSql,
                new
                {
                    FileId = id,
                    ConfigName = configName,
                    AttributesJson = attributesJson,
                    UpdatedAt = now
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (updatedAttributeRows <= 0)
            {
                string insertAttributesSql = $@"
INSERT INTO {attributeTable}
    (file_id, config_name, attributes_json, created_at, updated_at)
VALUES
    ({prefix}FileId, {prefix}ConfigName, {prefix}AttributesJson, {prefix}CreatedAt, {prefix}UpdatedAt)";
                await connection.ExecuteAsync(new CommandDefinition(
                    insertAttributesSql,
                    new
                    {
                        FileId = id,
                        ConfigName = configName,
                        AttributesJson = attributesJson,
                        CreatedAt = now,
                        UpdatedAt = now
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            string updateConfigSql = $@"
UPDATE {storageTable}
SET file_attribute_id = {prefix}ConfigName,
    updated_at = {prefix}UpdatedAt
WHERE id = {prefix}Id";
            await connection.ExecuteAsync(new CommandDefinition(
                updateConfigSql,
                new { Id = id, ConfigName = configName, UpdatedAt = now },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<GraphicDeleteResult?> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "图元 ID 必须大于 0。");

        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string storageTable = GetTableName(type, "CAD_FILE_STORAGE");
        string attributeTable = GetTableName(type, "CAD_BLOCK_ATTRIBUTES_JSON");
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        string selectSql = $@"
SELECT file_path AS FilePath,
       preview_image_path AS PreviewImagePath,
       category_type AS CategoryType,
       category_id AS CategoryId,
       file_stored_name AS FileStoredName,
       preview_image_name AS PreviewImageName
FROM {storageTable}
WHERE id = {prefix}Id";
        GraphicFileRow? row = await connection.QueryFirstOrDefaultAsync<GraphicFileRow>(new CommandDefinition(
            selectSql, new { Id = id }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row == null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        try
        {
            int deletedAttributeRows = await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {attributeTable} WHERE file_id = {prefix}Id",
                new { Id = id }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            int deletedStorageRows = await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {storageTable} WHERE id = {prefix}Id",
                new { Id = id }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (deletedStorageRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            int deletedPhysicalFiles = CleanupFiles(row);
            return new GraphicDeleteResult
            {
                Success = true,
                Message = "图元删除成功",
                DeletedAttributeRows = deletedAttributeRows,
                DeletedStorageRows = deletedStorageRows,
                DeletedPhysicalFiles = deletedPhysicalFiles
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private int CleanupFiles(GraphicFileRow row)
    {
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        AddSafePath(candidates, row.FilePath);
        AddSafePath(candidates, row.PreviewImagePath);

        string root = GetStorageRoot();
        if (!string.IsNullOrWhiteSpace(row.CategoryType) && row.CategoryId > 0)
        {
            string categoryDirectory = Path.Combine(root, row.CategoryType, row.CategoryId.ToString());
            if (!string.IsNullOrWhiteSpace(row.FileStoredName))
                AddSafePath(candidates, Path.Combine(categoryDirectory, row.FileStoredName));
            if (!string.IsNullOrWhiteSpace(row.PreviewImageName))
                AddSafePath(candidates, Path.Combine(categoryDirectory, row.PreviewImageName));
        }

        int count = 0;
        foreach (string path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    count++;
                }

                string backup = path + ".bak";
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "图元数据库删除成功，但物理文件清理失败。Path={Path}", path);
            }
        }

        return count;
    }

    private void AddSafePath(HashSet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string root = Path.GetFullPath(GetStorageRoot())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            candidates.Add(fullPath);
    }

    private sealed class GraphicFileRow
    {
        public string? FilePath { get; set; }
        public string? PreviewImagePath { get; set; }
        public string? CategoryType { get; set; }
        public int CategoryId { get; set; }
        public string? FileStoredName { get; set; }
        public string? PreviewImageName { get; set; }
    }

    private DbConnection CreateConnection(string type) => type == "MYSQL"
        ? new MySqlConnection(GetConnectionString("MYSQL"))
        : new DmConnection(GetConnectionString("DM"));

    private string GetTableName(string type, string tableName) => type == "MYSQL"
        ? tableName.ToLowerInvariant()
        : $"{GetSchemaName()}.{tableName}";

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

    private string GetStorageRoot()
    {
        string root = (_configuration["Storage:Root"]
            ?? _configuration["StorageSettings:RootPath"]
            ?? _configuration["UploadStorage:RootPath"]
            ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("未配置服务器图元存储根目录。");
        return root;
    }
}

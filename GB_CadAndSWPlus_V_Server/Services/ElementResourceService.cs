using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

public sealed class ElementResourceService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ElementResourceService> _logger;

    public ElementResourceService(IConfiguration configuration, ILogger<ElementResourceService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 更新平台资源的管理字段；文件替换由对应平台的文件服务单独处理。
    /// </summary>
    public async Task<ElementResourceResponse> UpdateAsync(long id, ElementResourceRequest request, string operatorName, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "资源 ID 必须大于 0。");
        Validate(request);
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        DateTime now = DateTime.Now;
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.IsDefault)
            {
                string clearDefaultSql = $"UPDATE {Table(type)} SET is_default=0, updated_at={prefix}Now WHERE element_definition_id={prefix}ElementId AND platform={prefix}Platform AND id<>{prefix}Id";
                await connection.ExecuteAsync(new CommandDefinition(clearDefaultSql, new
                {
                    ElementId = request.ElementDefinitionId,
                    Platform = request.Platform.Trim().ToUpperInvariant(),
                    Id = id,
                    Now = now
                }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            string sql = $@"UPDATE {Table(type)} SET resource_type={prefix}ResourceType,
 resource_name={prefix}ResourceName, resource_path={prefix}ResourcePath, file_hash={prefix}FileHash,
 configuration_name={prefix}ConfigurationName, status={prefix}Status, is_default={prefix}IsDefault,
 updated_by={prefix}OperatorName, updated_at={prefix}Now
 WHERE id={prefix}Id AND element_definition_id={prefix}ElementId AND platform={prefix}Platform";
            int count = await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = id,
                ElementId = request.ElementDefinitionId,
                Platform = request.Platform.Trim().ToUpperInvariant(),
                ResourceType = request.ResourceType.Trim().ToUpperInvariant(),
                ResourceName = request.ResourceName?.Trim(),
                ResourcePath = request.ResourcePath?.Trim(),
                FileHash = request.FileHash?.Trim(),
                ConfigurationName = request.ConfigurationName?.Trim(),
                Status = request.Status.Trim().ToUpperInvariant(),
                IsDefault = request.IsDefault ? 1 : 0,
                OperatorName = operatorName,
                Now = now
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (count == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new ElementResourceResponse { Success = false, Message = "SolidWorks 资源不存在或平台不匹配。" };
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation("构件平台资源更新成功。ResourceId={ResourceId}, ElementId={ElementId}, Platform={Platform}", id, request.ElementDefinitionId, request.Platform);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ElementResourceListResponse> ListAsync(long elementId, string? platform, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始读取构件平台资源数据。ElementId={ElementId}, Platform={Platform}", elementId, platform ?? "全部");
        if (elementId <= 0) throw new ArgumentOutOfRangeException(nameof(elementId), "构件 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        List<string> conditions = new List<string> { $"element_definition_id={prefix}ElementId" };
        DynamicParameters parameters = new DynamicParameters(new { ElementId = elementId });
        if (!string.IsNullOrWhiteSpace(platform))
        {
            conditions.Add($"platform={prefix}Platform");
            parameters.Add("Platform", platform.Trim().ToUpperInvariant());
        }

        string sql = SelectSql(type) + " WHERE " + string.Join(" AND ", conditions) + " ORDER BY is_default DESC, updated_at DESC, id";
        await using DbConnection connection = CreateConnection(type);
        _logger.LogInformation("准备打开资源数据库连接。DatabaseType={DatabaseType}, ElementId={ElementId}", type, elementId);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("资源数据库连接已打开，开始执行资源查询。ElementId={ElementId}", elementId);
        IReadOnlyList<ElementResourceDto> resources = (await connection.QueryAsync<ElementResourceDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        _logger.LogInformation("构件平台资源数据查询完成。ElementId={ElementId}, Platform={Platform}, ResourceCount={ResourceCount}", elementId, platform ?? "全部", resources.Count);
        return new ElementResourceListResponse { Success = true, Message = "构件资源查询成功。", Resources = resources };
    }

    public async Task<ElementResourceResponse> CreateAsync(ElementResourceRequest request, string operatorName, CancellationToken cancellationToken)
    {
        Validate(request);
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DateTime now = DateTime.Now;
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (request.IsDefault)
            {
                string clearDefaultSql = $"UPDATE {Table(type)} SET is_default=0, updated_at={prefix}Now WHERE element_definition_id={prefix}ElementId AND platform={prefix}Platform";
                await connection.ExecuteAsync(new CommandDefinition(clearDefaultSql, new { ElementId = request.ElementDefinitionId, Platform = request.Platform.Trim().ToUpperInvariant(), Now = now }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            string insertSql = $@"INSERT INTO {Table(type)}
 (id, element_definition_id, platform, resource_type, file_storage_id, resource_name, resource_path, file_hash, version, configuration_name, status, is_default, created_by, updated_by, created_at, updated_at)
 VALUES ({prefix}Id, {prefix}ElementId, {prefix}Platform, {prefix}ResourceType, {prefix}FileStorageId, {prefix}ResourceName, {prefix}ResourcePath, {prefix}FileHash, 1, {prefix}ConfigurationName, {prefix}Status, {prefix}IsDefault, {prefix}OperatorName, {prefix}OperatorName, {prefix}Now, {prefix}Now)";
            await connection.ExecuteAsync(new CommandDefinition(insertSql, new
            {
                Id = id,
                ElementId = request.ElementDefinitionId,
                Platform = request.Platform.Trim().ToUpperInvariant(),
                ResourceType = request.ResourceType.Trim().ToUpperInvariant(),
                request.FileStorageId,
                ResourceName = request.ResourceName?.Trim(),
                ResourcePath = request.ResourcePath?.Trim(),
                FileHash = request.FileHash?.Trim(),
                ConfigurationName = request.ConfigurationName?.Trim(),
                Status = request.Status.Trim().ToUpperInvariant(),
                IsDefault = request.IsDefault ? 1 : 0,
                OperatorName = operatorName,
                Now = now
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation("构件平台资源绑定成功。ElementId={ElementId}, ResourceId={ResourceId}, Platform={Platform}", request.ElementDefinitionId, id, request.Platform);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ElementResourceResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "资源 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        ElementResourceDto? resource = await connection.QuerySingleOrDefaultAsync<ElementResourceDto>(
            new CommandDefinition(SelectSql(type) + $" WHERE id={prefix}Id", new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return resource == null
            ? new ElementResourceResponse { Success = false, Message = "资源不存在。" }
            : new ElementResourceResponse { Success = true, Message = "资源查询成功。", Resource = resource };
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "资源 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string sql = $"UPDATE {Table(type)} SET status='ARCHIVED', is_default=0, updated_at={prefix}Now WHERE id={prefix}Id";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Now = DateTime.Now }, cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    private static void Validate(ElementResourceRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.ElementDefinitionId <= 0) throw new ArgumentException("构件 ID 必须大于 0。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Platform) || (request.Platform.Trim().ToUpperInvariant() != "CAD" && request.Platform.Trim().ToUpperInvariant() != "SOLIDWORKS")) throw new ArgumentException("资源平台必须是 CAD 或 SOLIDWORKS。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ResourceType)) throw new ArgumentException("资源类型不能为空。", nameof(request));
        if (!request.FileStorageId.HasValue && string.IsNullOrWhiteSpace(request.ResourcePath)) throw new ArgumentException("资源必须绑定 FileStorageId 或资源路径。", nameof(request));
        string[] statuses = { "DRAFT", "REVIEW", "PUBLISHED", "DISABLED", "ARCHIVED" };
        if (Array.IndexOf(statuses, request.Status.Trim().ToUpperInvariant()) < 0) throw new ArgumentException("资源状态无效。", nameof(request));
    }

    private string SelectSql(string type) => $@"SELECT id AS Id, element_definition_id AS ElementDefinitionId, platform AS Platform,
 resource_type AS ResourceType, file_storage_id AS FileStorageId, resource_name AS ResourceName,
 resource_name AS OriginalFileName,
 resource_path AS ResourcePath, file_hash AS FileHash, version AS Version, configuration_name AS ConfigurationName,
 status AS Status, is_default AS IsDefault, created_by AS CreatedBy, updated_by AS UpdatedBy,
 created_at AS CreatedAt, updated_at AS UpdatedAt FROM {Table(type)}";
    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private string Table(string type) => type == "MYSQL" ? "element_resources" : $"{GetSchemaName()}.ELEMENT_RESOURCES";
    private DbConnection CreateConnection(string type) => type == "MYSQL" ? new MySqlConnection(GetConnectionString("MYSQL")) : new DmConnection(GetConnectionString("DM"));
    private string GetSchemaName() => (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
    private string GetConnectionString(string type) => (_configuration[$"ConnectionStrings:{(type == "MYSQL" ? "MySQL" : "DM")}"] ?? _configuration["Database:ConnectionString"] ?? string.Empty).Trim();
}

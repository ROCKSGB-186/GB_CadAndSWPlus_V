using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

public sealed class ElementDefinitionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ElementDefinitionService> _logger;

    public ElementDefinitionService(IConfiguration configuration, ILogger<ElementDefinitionService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ElementDefinitionListResponse> ListAsync(
        int? categoryId,
        string? platform,
        string? keyword,
        string? status,
        CancellationToken cancellationToken)
    {
        string type = GetDatabaseType();
        string table = Table(type);
        string resourceTable = ResourceTable(type);
        string prefix = type == "MYSQL" ? "@" : ":";
        List<string> conditions = new List<string>();
        DynamicParameters parameters = new DynamicParameters();

        if (categoryId.HasValue)
        {
            conditions.Add($"e.category_id = {prefix}CategoryId");
            parameters.Add("CategoryId", categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add($"(e.element_code LIKE {prefix}Keyword OR e.name LIKE {prefix}Keyword OR e.display_name LIKE {prefix}Keyword)");
            parameters.Add("Keyword", "%" + keyword.Trim() + "%");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            conditions.Add($"e.status = {prefix}Status");
            parameters.Add("Status", status.Trim().ToUpperInvariant());
        }

        // SolidWorks/CAD 共用图元定义，但平台资源不同；查询 SolidWorks 时只返回已绑定 SolidWorks 模型资源的图元。
        if (!string.IsNullOrWhiteSpace(platform))
        {
            conditions.Add($"EXISTS (SELECT 1 FROM {resourceTable} r WHERE r.element_definition_id = e.id AND r.platform = {prefix}Platform)");
            parameters.Add("Platform", platform.Trim().ToUpperInvariant());
        }

        string where = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
        string sql = $@"SELECT id AS Id, element_code AS ElementCode, name AS Name,
 display_name AS DisplayName, category_id AS CategoryId, element_type AS ElementType,
 discipline AS Discipline, standard_no AS StandardNo, description AS Description,
 status AS Status, current_version AS CurrentVersion, created_by AS CreatedBy,
 updated_by AS UpdatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt
 FROM {table} e{where} ORDER BY e.updated_at DESC, e.id";

        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ElementDefinitionDto> elements = (await connection.QueryAsync<ElementDefinitionDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();

        _logger.LogInformation("统一构件定义查询完成。CategoryId={CategoryId}, Platform={Platform}, Count={Count}", categoryId, platform, elements.Count);
        return new ElementDefinitionListResponse { Success = true, Message = "构件查询成功。", Elements = elements };
    }

    public async Task<ElementDefinitionResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "构件 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string sql = $@"SELECT id AS Id, element_code AS ElementCode, name AS Name,
 display_name AS DisplayName, category_id AS CategoryId, element_type AS ElementType,
 discipline AS Discipline, standard_no AS StandardNo, description AS Description,
 status AS Status, current_version AS CurrentVersion, created_by AS CreatedBy,
 updated_by AS UpdatedBy, created_at AS CreatedAt, updated_at AS UpdatedAt
 FROM {Table(type)} WHERE id = {prefix}Id";

        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        ElementDefinitionDto? element = await connection.QuerySingleOrDefaultAsync<ElementDefinitionDto>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return element == null
            ? new ElementDefinitionResponse { Success = false, Message = "构件不存在。" }
            : new ElementDefinitionResponse { Success = true, Message = "构件查询成功。", Element = element };
    }

    public async Task<ElementDefinitionResponse> CreateAsync(ElementDefinitionRequest request, string operatorName, CancellationToken cancellationToken)
    {
        Validate(request);
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DateTime now = DateTime.Now;
        string sql = $@"INSERT INTO {Table(type)}
 (id, element_code, name, display_name, category_id, element_type, discipline, standard_no, description, status, current_version, created_by, updated_by, created_at, updated_at)
 VALUES ({prefix}Id, {prefix}ElementCode, {prefix}Name, {prefix}DisplayName, {prefix}CategoryId, {prefix}ElementType, {prefix}Discipline, {prefix}StandardNo, {prefix}Description, {prefix}Status, 1, {prefix}OperatorName, {prefix}OperatorName, {prefix}Now, {prefix}Now)";

        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = id,
                request.ElementCode,
                request.Name,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Name : request.DisplayName.Trim(),
                request.CategoryId,
                request.ElementType,
                request.Discipline,
                request.StandardNo,
                request.Description,
                Status = request.Status.Trim().ToUpperInvariant(),
                OperatorName = operatorName,
                Now = now
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex, "新增统一构件失败，可能存在重复构件编码。ElementCode={ElementCode}", request.ElementCode);
            throw new InvalidOperationException("构件新增失败，构件编码可能已存在。", ex);
        }

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ElementDefinitionResponse> UpdateAsync(long id, ElementDefinitionRequest request, string operatorName, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "构件 ID 必须大于 0。");
        Validate(request);
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string sql = $@"UPDATE {Table(type)} SET element_code={prefix}ElementCode, name={prefix}Name,
 display_name={prefix}DisplayName, category_id={prefix}CategoryId, element_type={prefix}ElementType,
 discipline={prefix}Discipline, standard_no={prefix}StandardNo, description={prefix}Description,
 status={prefix}Status, current_version=current_version+1, updated_by={prefix}OperatorName, updated_at={prefix}Now
 WHERE id={prefix}Id";

        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        int count = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            request.ElementCode,
            request.Name,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Name : request.DisplayName.Trim(),
            request.CategoryId,
            request.ElementType,
            request.Discipline,
            request.StandardNo,
            request.Description,
            Status = request.Status.Trim().ToUpperInvariant(),
            OperatorName = operatorName,
            Now = DateTime.Now
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (count == 0) return new ElementDefinitionResponse { Success = false, Message = "构件不存在。" };
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "构件 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string sql = $"UPDATE {Table(type)} SET status='ARCHIVED', current_version=current_version+1, updated_at={prefix}Now WHERE id={prefix}Id";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, Now = DateTime.Now }, cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }

    private static void Validate(ElementDefinitionRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ElementCode)) throw new ArgumentException("构件编码不能为空。", nameof(request));
        if (request.ElementCode.Length > 128) throw new ArgumentException("构件编码不能超过 128 个字符。", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("构件名称不能为空。", nameof(request));
        string[] statuses = { "DRAFT", "REVIEW", "PUBLISHED", "DISABLED", "ARCHIVED" };
        if (Array.IndexOf(statuses, request.Status.Trim().ToUpperInvariant()) < 0) throw new ArgumentException("构件状态无效。", nameof(request));
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private string Table(string type) => type == "MYSQL" ? "element_definitions" : $"{GetSchemaName()}.ELEMENT_DEFINITIONS";
    private string ResourceTable(string type) => type == "MYSQL" ? "element_resources" : $"{GetSchemaName()}.ELEMENT_RESOURCES";
    private DbConnection CreateConnection(string type) => type == "MYSQL" ? new MySqlConnection(GetConnectionString("MYSQL")) : new DmConnection(GetConnectionString("DM"));
    private string GetSchemaName() => (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
    private string GetConnectionString(string type) => (_configuration[$"ConnectionStrings:{(type == "MYSQL" ? "MySQL" : "DM")}"] ?? _configuration["Database:ConnectionString"] ?? string.Empty).Trim();
}

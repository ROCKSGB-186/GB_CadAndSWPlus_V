using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Text.Json;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

public sealed class SwInstanceService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SwInstanceService> _logger;

    public SwInstanceService(IConfiguration configuration, ILogger<SwInstanceService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SwInstanceResponse> RegisterAsync(SwInstanceRegisterRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.SwInstanceId) || string.IsNullOrWhiteSpace(request.PidInstanceId))
            return Failure("SwInstanceId 和 PidInstanceId 不能为空。");

        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string table = Table(type);
        DateTime now = DateTime.Now;
        string propertiesJson = JsonSerializer.Serialize(request.CustomProperties ?? new Dictionary<string, string>());
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        string existsSql = $"SELECT COUNT(1) FROM {table} WHERE sw_instance_id={prefix}SwInstanceId";
        int exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(existsSql, new { request.SwInstanceId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (exists > 0)
        {
            if (!request.HasExpectedVersion)
                return Failure("更新已有 SolidWorks 实例时必须提供期望版本。", true);

            string updateSql = $@"UPDATE {table} SET pid_instance_id={prefix}PidInstanceId, library_item_id={prefix}LibraryItemId,
 document_path={prefix}DocumentPath, document_name={prefix}DocumentName, assembly_name={prefix}AssemblyName,
 component_name={prefix}ComponentName, configuration_name={prefix}ConfigurationName, transform_json={prefix}TransformJson,
 custom_properties_json={prefix}CustomPropertiesJson, version={prefix}Version, sync_status={prefix}SyncStatus, updated_at={prefix}UpdatedAt
 WHERE sw_instance_id={prefix}SwInstanceId AND version={prefix}ExpectedVersion";
            int updatedRows = await connection.ExecuteAsync(new CommandDefinition(updateSql, new
            {
                request.SwInstanceId, request.PidInstanceId, request.LibraryItemId, request.DocumentPath, request.DocumentName,
                request.AssemblyName, request.ComponentName, request.ConfigurationName, request.TransformJson,
                CustomPropertiesJson = propertiesJson, Version = request.Version + 1, SyncStatus = "SYNCED", UpdatedAt = now,
                ExpectedVersion = request.Version
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (updatedRows == 0)
                return Failure("SolidWorks 实例版本已变化，请先重新加载后再提交。", true);
        }
        else
        {
            string insertSql = $@"INSERT INTO {table}
(sw_instance_id,pid_instance_id,library_item_id,document_path,document_name,assembly_name,component_name,configuration_name,transform_json,custom_properties_json,version,sync_status,created_at,updated_at)
VALUES ({prefix}SwInstanceId,{prefix}PidInstanceId,{prefix}LibraryItemId,{prefix}DocumentPath,{prefix}DocumentName,{prefix}AssemblyName,{prefix}ComponentName,{prefix}ConfigurationName,{prefix}TransformJson,{prefix}CustomPropertiesJson,{prefix}Version,{prefix}SyncStatus,{prefix}CreatedAt,{prefix}UpdatedAt)";
            await connection.ExecuteAsync(new CommandDefinition(insertSql, new
            {
                request.SwInstanceId, request.PidInstanceId, request.LibraryItemId, request.DocumentPath, request.DocumentName,
                request.AssemblyName, request.ComponentName, request.ConfigurationName, request.TransformJson,
                CustomPropertiesJson = propertiesJson, Version = Math.Max(1, request.Version), SyncStatus = "SYNCED", CreatedAt = now, UpdatedAt = now
            }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        return await GetAsync(request.SwInstanceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SwInstanceResponse> GetAsync(string swInstanceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(swInstanceId)) return Failure("SwInstanceId 不能为空。");
        string type = GetDatabaseType(); string prefix = type == "MYSQL" ? "@" : ":"; string table = Table(type);
        string sql = $@"SELECT sw_instance_id AS SwInstanceId,pid_instance_id AS PidInstanceId,library_item_id AS LibraryItemId,
 document_path AS DocumentPath,document_name AS DocumentName,assembly_name AS AssemblyName,component_name AS ComponentName,
 configuration_name AS ConfigurationName,transform_json AS TransformJson,custom_properties_json AS CustomPropertiesJson,
 version AS Version,sync_status AS SyncStatus,updated_at AS UpdatedAt FROM {table} WHERE sw_instance_id={prefix}SwInstanceId";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        SwInstanceRow? row = await connection.QuerySingleOrDefaultAsync<SwInstanceRow>(new CommandDefinition(sql, new { SwInstanceId = swInstanceId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row == null) return new SwInstanceResponse { Success = false, Message = "SolidWorks 实例不存在。" };
        Dictionary<string, string> properties;
        try { properties = JsonSerializer.Deserialize<Dictionary<string, string>>(row.CustomPropertiesJson ?? "{}") ?? new(); } catch { properties = new(); }
        return new SwInstanceResponse { Success = true, Message = "查询成功", Instance = new SwInstanceDto {
            SwInstanceId = row.SwInstanceId, PidInstanceId = row.PidInstanceId, LibraryItemId = row.LibraryItemId,
            DocumentPath = row.DocumentPath ?? string.Empty, DocumentName = row.DocumentName ?? string.Empty, AssemblyName = row.AssemblyName ?? string.Empty,
            ComponentName = row.ComponentName ?? string.Empty, ConfigurationName = row.ConfigurationName ?? string.Empty, TransformJson = row.TransformJson ?? string.Empty,
            CustomProperties = properties, Version = row.Version, SyncStatus = row.SyncStatus ?? string.Empty, UpdatedAt = row.UpdatedAt
        }};
    }

    private static SwInstanceResponse Failure(string message, bool conflict = false) => new() { Success = false, Message = message, Conflict = conflict };
    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private string Table(string type) => type == "MYSQL" ? "sw_model_instances" : $"{Schema()}.SW_MODEL_INSTANCES";
    private string Schema() => (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
    private DbConnection CreateConnection(string type) => type == "MYSQL" ? new MySqlConnection(Connection(type)) : new DmConnection(Connection(type));
    private string Connection(string type) => (_configuration["Database:ConnectionString"] ?? _configuration.GetConnectionString(type == "MYSQL" ? "MySQL" : "DM") ?? string.Empty).Trim();
    private sealed class SwInstanceRow { public string SwInstanceId { get; set; } = string.Empty; public string PidInstanceId { get; set; } = string.Empty; public int? LibraryItemId { get; set; } public string? DocumentPath { get; set; } public string? DocumentName { get; set; } public string? AssemblyName { get; set; } public string? ComponentName { get; set; } public string? ConfigurationName { get; set; } public string? TransformJson { get; set; } public string? CustomPropertiesJson { get; set; } public int Version { get; set; } public string? SyncStatus { get; set; } public DateTime UpdatedAt { get; set; } }
}

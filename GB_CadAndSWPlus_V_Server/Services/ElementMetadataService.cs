using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>统一构件属性定义和值的查询与保存服务。</summary>
public sealed class ElementMetadataService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ElementMetadataService> _logger;

    public ElementMetadataService(IConfiguration configuration, ILogger<ElementMetadataService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>>> ListAsync(long elementId, CancellationToken cancellationToken)
    {
        if (elementId <= 0) throw new ArgumentOutOfRangeException(nameof(elementId), "构件 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string tablePrefix = GetTablePrefix(type);
        string sql = $@"SELECT d.id AS Id, d.element_definition_id AS ElementDefinitionId,
 d.property_code AS PropertyCode, d.property_name AS PropertyName, d.data_type AS DataType,
 d.unit AS Unit, d.is_required AS IsRequired, d.sort_order AS SortOrder,
 v.value_text AS ValueText, v.value_number AS ValueNumber, v.value_bool AS ValueBool
 FROM {tablePrefix}ELEMENT_PROPERTY_DEFINITIONS d
 LEFT JOIN {tablePrefix}ELEMENT_PROPERTY_VALUES v
   ON v.element_definition_id=d.element_definition_id AND v.property_definition_id=d.id
 WHERE d.element_definition_id={prefix}ElementId
 ORDER BY d.sort_order, d.id";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ElementPropertyValueDto> data = (await connection.QueryAsync<ElementPropertyValueDto>(new CommandDefinition(sql, new { ElementId = elementId }, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        return new ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>> { Success = true, Message = "构件属性查询成功。", Data = data };
    }

    public async Task<ElementMetadataResponse<object>> SaveAsync(long elementId, IReadOnlyList<ElementPropertyValueRequest> values, string operatorName, CancellationToken cancellationToken)
    {
        if (elementId <= 0) throw new ArgumentOutOfRangeException(nameof(elementId), "构件 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string tablePrefix = GetTablePrefix(type);
        DateTime now = DateTime.Now;
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (ElementPropertyValueRequest value in values ?? Array.Empty<ElementPropertyValueRequest>())
            {
                string existsSql = $@"SELECT COUNT(1) FROM {tablePrefix}ELEMENT_PROPERTY_VALUES
 WHERE element_definition_id={prefix}ElementId AND property_definition_id={prefix}PropertyDefinitionId";
                int exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(existsSql, new { ElementId = elementId, PropertyDefinitionId = value.PropertyDefinitionId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (exists > 0)
                {
                    string updateSql = $@"UPDATE {tablePrefix}ELEMENT_PROPERTY_VALUES SET value_text={prefix}ValueText,
 source='MANUAL', version=version+1, updated_at={prefix}Now
 WHERE element_definition_id={prefix}ElementId AND property_definition_id={prefix}PropertyDefinitionId";
                    await connection.ExecuteAsync(new CommandDefinition(updateSql, new { ElementId = elementId, PropertyDefinitionId = value.PropertyDefinitionId, ValueText = value.ValueText, Now = now }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
                else
                {
                    string insertSql = $@"INSERT INTO {tablePrefix}ELEMENT_PROPERTY_VALUES
 (id, element_definition_id, property_definition_id, value_text, source, version, created_at, updated_at)
 VALUES ({prefix}Id, {prefix}ElementId, {prefix}PropertyDefinitionId, {prefix}ValueText, 'MANUAL', 1, {prefix}Now, {prefix}Now)";
                    long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(1, 999);
                    await connection.ExecuteAsync(new CommandDefinition(insertSql, new { Id = id, ElementId = elementId, PropertyDefinitionId = value.PropertyDefinitionId, ValueText = value.ValueText, Now = now }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation("统一构件属性保存成功。ElementId={ElementId}, Count={Count}, Operator={Operator}", elementId, values?.Count ?? 0, operatorName);
        return new ElementMetadataResponse<object> { Success = true, Message = "构件属性保存成功。", Data = new { } };
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private string GetTablePrefix(string type) => type == "MYSQL" ? string.Empty : $"{(_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant()}.";
    private DbConnection CreateConnection(string type) => type == "MYSQL" ? new MySqlConnection(_configuration["Database:ConnectionString"]) : new DmConnection(_configuration["Database:ConnectionString"]);
}

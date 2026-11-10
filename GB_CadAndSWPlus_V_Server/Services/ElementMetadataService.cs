using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Text.RegularExpressions;

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
        string definitionsTable = type == "MYSQL" ? "element_property_definitions" : $"{GetTablePrefix(type)}ELEMENT_PROPERTY_DEFINITIONS";
        string valuesTable = type == "MYSQL" ? "element_property_values" : $"{GetTablePrefix(type)}ELEMENT_PROPERTY_VALUES";
        string sql = $@"SELECT d.id AS Id, d.element_definition_id AS ElementDefinitionId,
 d.property_code AS PropertyCode, d.property_name AS PropertyName, d.data_type AS DataType,
 d.unit AS Unit, d.is_required AS IsRequired, d.sort_order AS SortOrder,
 v.value_text AS ValueText, v.value_number AS ValueNumber, v.value_bool AS ValueBool
 FROM {definitionsTable} d
 LEFT JOIN {valuesTable} v
   ON v.element_definition_id=d.element_definition_id AND v.property_definition_id=d.id
 WHERE d.element_definition_id={prefix}ElementId
 ORDER BY d.sort_order, d.id";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ElementPropertyValueDto> data = (await connection.QueryAsync<ElementPropertyValueDto>(new CommandDefinition(sql, new { ElementId = elementId }, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
        return new ElementMetadataResponse<IReadOnlyList<ElementPropertyValueDto>> { Success = true, Message = "构件属性查询成功。", Data = data };
    }

    public async Task<ElementMetadataResponse<IReadOnlyList<ElementPropertyDto>>> CreateDefinitionsAsync(
        long elementId, IReadOnlyList<CreateElementPropertyDefinitionRequest?>? requests, string operatorName, CancellationToken cancellationToken)
    {
        // 输入在打开连接前完整校验；数据库异常只记录类别，不记录连接信息。
        IReadOnlyList<ElementPropertyDto> definitions = ElementPropertyDefinitionCreation.Validate(elementId, requests);
        _logger.LogInformation("开始新增统一构件属性定义。构件ID={ElementId}，数量={Count}，操作人={Operator}", elementId, definitions.Count, operatorName);
        string type = GetDatabaseType();
        string schema = type == "MYSQL" ? string.Empty : (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant();
        if (type != "MYSQL" && !Regex.IsMatch(schema, @"\A[A-Z][A-Z0-9_]{0,127}\z"))
            throw new InvalidOperationException("数据库模式配置无效。");
        string connectionString;
        try
        {
            connectionString = ElementPropertyDefinitionCreation.ValidateConnectionStrings(
                _configuration.GetConnectionString(type == "MYSQL" ? "MySQL" : "DM"),
                _configuration["Database:ConnectionString"]);
        }
        catch (ElementPropertyDefinitionDatabaseException)
        {
            _logger.LogError("统一构件与属性的数据库连接配置不一致或缺失，已禁止新增属性定义。");
            throw;
        }

        try
        {
            await using DbConnection connection = type == "MYSQL" ? new MySqlConnection(connectionString) : new DmConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using DbTransaction dbTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var transaction = new DefinitionTransaction(connection, dbTransaction, type, schema, _logger);
            IReadOnlyList<ElementPropertyDto> created = await ElementPropertyDefinitionCreation.ExecuteAsync(elementId, definitions, transaction, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("统一构件属性定义事务提交成功。构件ID={ElementId}，数量={Count}，操作人={Operator}", elementId, created.Count, operatorName);
            return new ElementMetadataResponse<IReadOnlyList<ElementPropertyDto>> { Success = true, Message = "构件属性定义新增成功。", Data = created };
        }
        catch (DbException ex) when (IsDuplicateKey(ex))
        {
            _logger.LogWarning("新增统一构件属性定义失败：数据库唯一约束冲突。构件ID={ElementId}，操作人={Operator}", elementId, operatorName);
            throw new ElementPropertyDefinitionConflictException("属性定义编码或生成的 ID 与现有数据冲突，请检查后重试。");
        }
        catch (DbException)
        {
            _logger.LogError("新增统一构件属性定义时数据库操作失败，事务结果需要核实。构件ID={ElementId}，操作人={Operator}", elementId, operatorName);
            throw new ElementPropertyDefinitionDatabaseException();
        }
        catch (ArgumentException)
        {
            _logger.LogError("新增统一构件属性定义时数据库参数无效。构件ID={ElementId}，操作人={Operator}", elementId, operatorName);
            throw new ElementPropertyDefinitionDatabaseException();
        }
    }

    private static bool IsDuplicateKey(DbException exception) =>
        exception is MySqlException mysql && mysql.Number == 1062
        || exception is DmException dm && dm.Number == -6602;

    private sealed class DefinitionTransaction : IElementPropertyDefinitionTransaction
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction _transaction;
        private readonly string _parameter;
        private readonly string _elements;
        private readonly string _definitions;
        private readonly ILogger _logger;
        private readonly DateTime _now = DateTime.Now;

        public DefinitionTransaction(DbConnection connection, DbTransaction transaction, string type, string schema, ILogger logger)
        {
            _connection = connection;
            _transaction = transaction;
            _logger = logger;
            _parameter = type == "MYSQL" ? "@" : ":";
            _elements = type == "MYSQL" ? "element_definitions" : $"{schema}.ELEMENT_DEFINITIONS";
            _definitions = type == "MYSQL" ? "element_property_definitions" : $"{schema}.ELEMENT_PROPERTY_DEFINITIONS";
        }

        public async Task<string?> LockElementAsync(long elementId, CancellationToken cancellationToken)
        {
            string? status = await _connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                $"SELECT status FROM {_elements} WHERE id={_parameter}ElementId FOR UPDATE",
                new { ElementId = elementId }, _transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            _logger.LogInformation("已检查并锁定统一构件。构件ID={ElementId}，构件存在={Exists}", elementId, status != null);
            return status;
        }

        public async Task<IReadOnlyList<ElementPropertyDto>> ListDefinitionsAsync(long elementId, CancellationToken cancellationToken)
        {
            var existing = (await _connection.QueryAsync<ElementPropertyDto>(new CommandDefinition(
                $"SELECT property_code AS PropertyCode, property_name AS PropertyName FROM {_definitions} WHERE element_definition_id={_parameter}ElementId",
                new { ElementId = elementId }, _transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
            _logger.LogInformation("已查询构件已有属性定义。构件ID={ElementId}，数量={Count}", elementId, existing.Count);
            return existing;
        }

        public async Task InsertAsync(ElementPropertyDto item, CancellationToken cancellationToken)
        {
            string p = _parameter;
            string sql = $@"INSERT INTO {_definitions}
 (id, element_definition_id, property_code, property_name, data_type, unit, is_required, sort_order, created_at, updated_at)
 VALUES ({p}Id, {p}ElementDefinitionId, {p}PropertyCode, {p}PropertyName, {p}DataType, {p}Unit, {p}IsRequired, {p}SortOrder, {p}Now, {p}Now)";
            await _connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                item.Id, item.ElementDefinitionId, item.PropertyCode, item.PropertyName, item.DataType,
                Unit = string.IsNullOrEmpty(item.Unit) ? null : item.Unit, IsRequired = item.IsRequired ? 1 : 0,
                item.SortOrder, Now = _now
            }, _transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            _logger.LogInformation("属性定义已写入事务，尚未提交。构件ID={ElementId}，属性ID={PropertyId}", item.ElementDefinitionId, item.Id);
        }

        public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);
        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("统一构件属性定义事务已回滚。");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public async Task<ElementMetadataResponse<object>> SaveAsync(long elementId, IReadOnlyList<ElementPropertyValueRequest> values, string operatorName, CancellationToken cancellationToken)
    {
        if (elementId <= 0) throw new ArgumentOutOfRangeException(nameof(elementId), "构件 ID 必须大于 0。");
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        string valuesTable = type == "MYSQL" ? "element_property_values" : $"{GetTablePrefix(type)}ELEMENT_PROPERTY_VALUES";
        DateTime now = DateTime.Now;
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (ElementPropertyValueRequest value in values ?? Array.Empty<ElementPropertyValueRequest>())
            {
                string existsSql = $@"SELECT COUNT(1) FROM {valuesTable}
 WHERE element_definition_id={prefix}ElementId AND property_definition_id={prefix}PropertyDefinitionId";
                int exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(existsSql, new { ElementId = elementId, PropertyDefinitionId = value.PropertyDefinitionId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                if (exists > 0)
                {
                    string updateSql = $@"UPDATE {valuesTable} SET value_text={prefix}ValueText,
 source='MANUAL', version=version+1, updated_at={prefix}Now
 WHERE element_definition_id={prefix}ElementId AND property_definition_id={prefix}PropertyDefinitionId";
                    await connection.ExecuteAsync(new CommandDefinition(updateSql, new { ElementId = elementId, PropertyDefinitionId = value.PropertyDefinitionId, ValueText = value.ValueText, Now = now }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
                else
                {
                    string insertSql = $@"INSERT INTO {valuesTable}
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

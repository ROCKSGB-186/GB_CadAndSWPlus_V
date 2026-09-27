using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using System.Text.Json;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>
/// 图层字典业务服务。
/// 所有 DM 连接和事务均在服务器端执行。
/// </summary>
public sealed class LayerDictionaryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LayerDictionaryService> _logger;

    public LayerDictionaryService(
        IConfiguration configuration,
        ILogger<LayerDictionaryService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<LayerDictionaryEntryDto>> GetPersonalAsync(string username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("用户名不能为空。", nameof(username));

        const string sql = @"
SELECT ID, SEQ, MAJOR, USERNAME, USER_ID, SOURCE, MAPPINGS_JSON,
       CREATED_BY, CREATED_AT, UPDATED_AT, IS_ACTIVE
FROM LAYER_DICTIONARY
WHERE USERNAME = :Username AND IS_ACTIVE = 1
ORDER BY SEQ";

        return await QueryAsync(sql, command => AddParameter(command, "Username", username.Trim()), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<List<LayerDictionaryEntryDto>> GetStandardAsync(string? major, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT ID, SEQ, MAJOR, USERNAME, USER_ID, SOURCE, MAPPINGS_JSON,
       CREATED_BY, CREATED_AT, UPDATED_AT, IS_ACTIVE
FROM LAYER_DICTIONARY
WHERE USERNAME IN ('sa', 'root', 'admin')
  AND (:Major IS NULL OR :Major = '' OR MAJOR = :Major)
  AND IS_ACTIVE = 1
ORDER BY SEQ";

        return await QueryAsync(sql, command => AddParameter(
            command,
            "Major",
            string.IsNullOrWhiteSpace(major) ? DBNull.Value : major.Trim()), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LayerDictionaryMutationResponse> SavePersonalAsync(
        SaveLayerDictionaryRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Username))
            throw new ArgumentException("保存图层字典时用户名不能为空。", nameof(request));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM LAYER_DICTIONARY WHERE USERNAME = :Username";
                AddParameter(deleteCommand, "Username", request.Username.Trim());
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            int affectedRows = 0;
            foreach (LayerDictionaryEntryDto entry in request.Entries ?? new List<LayerDictionaryEntryDto>())
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = @"
INSERT INTO LAYER_DICTIONARY
(SEQ, MAJOR, USERNAME, USER_ID, SOURCE, MAPPINGS_JSON, CREATED_BY,
 CREATED_AT, UPDATED_AT, IS_ACTIVE)
VALUES
(:Seq, :Major, :Username, :UserId, :Source, :MappingsJson, :CreatedBy,
 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1)";

                AddParameter(insertCommand, "Seq", entry.Seq);
                AddParameter(insertCommand, "Major", NullIfEmpty(entry.Major));
                AddParameter(insertCommand, "Username", request.Username.Trim());
                AddParameter(insertCommand, "UserId", entry.UserId.HasValue ? entry.UserId.Value : DBNull.Value);
                AddParameter(insertCommand, "Source", string.IsNullOrWhiteSpace(entry.Source) ? "personal" : entry.Source);
                AddParameter(insertCommand, "MappingsJson", GetMappingsJson(entry));
                AddParameter(insertCommand, "CreatedBy", string.IsNullOrWhiteSpace(entry.CreatedBy)
                    ? request.Username.Trim()
                    : entry.CreatedBy);
                affectedRows += await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("个人图层字典保存成功。Username={Username}, EntryCount={EntryCount}", request.Username.Trim(), affectedRows);
            return new LayerDictionaryMutationResponse
            {
                Success = true,
                Message = "个人图层字典保存成功。",
                AffectedRows = affectedRows
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<LayerDictionaryMutationResponse> DeleteAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken)
    {
        int[] idList = (ids ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToArray();
        if (idList.Length == 0)
        {
            return new LayerDictionaryMutationResponse
            {
                Success = true,
                Message = "没有需要删除的图层字典记录。"
            };
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        List<string> parameterNames = new();
        for (int index = 0; index < idList.Length; index++)
        {
            string parameterName = "Id" + index;
            parameterNames.Add(":" + parameterName);
            AddParameter(command, parameterName, idList[index]);
        }

        command.CommandText = $"DELETE FROM LAYER_DICTIONARY WHERE ID IN ({string.Join(",", parameterNames)})";
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("图层字典删除成功。RequestedCount={RequestedCount}, AffectedRows={AffectedRows}", idList.Length, affectedRows);
        return new LayerDictionaryMutationResponse
        {
            Success = true,
            Message = "图层字典删除成功。",
            AffectedRows = affectedRows
        };
    }

    private async Task<List<LayerDictionaryEntryDto>> QueryAsync(
        string sql,
        Action<System.Data.IDbCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        configureParameters(command);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<LayerDictionaryEntryDto> result = new();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(MapEntry(reader));
        }

        return result;
    }

    private async Task<DmConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = (_configuration.GetConnectionString("DM") ?? string.Empty).Trim();
        }
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("服务器未配置达梦数据库连接字符串。");

        DmConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            string schema = GetSchemaName();
            using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = $"SET SCHEMA \"{schema}\"";
            schemaCommand.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || schema.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException("服务器数据库 Schema 配置无效。");

        return schema.ToUpperInvariant();
    }

    private static LayerDictionaryEntryDto MapEntry(System.Data.IDataRecord reader)
    {
        return new LayerDictionaryEntryDto
        {
            Id = Convert.ToInt32(reader["ID"]),
            Seq = Convert.ToInt32(reader["SEQ"]),
            Major = reader["MAJOR"] == DBNull.Value ? string.Empty : Convert.ToString(reader["MAJOR"]) ?? string.Empty,
            Username = reader["USERNAME"] == DBNull.Value ? string.Empty : Convert.ToString(reader["USERNAME"]) ?? string.Empty,
            UserId = reader["USER_ID"] == DBNull.Value ? null : Convert.ToInt32(reader["USER_ID"]),
            Source = reader["SOURCE"] == DBNull.Value ? string.Empty : Convert.ToString(reader["SOURCE"]) ?? string.Empty,
            MappingsJson = reader["MAPPINGS_JSON"] == DBNull.Value ? string.Empty : Convert.ToString(reader["MAPPINGS_JSON"]) ?? string.Empty,
            CreatedBy = reader["CREATED_BY"] == DBNull.Value ? string.Empty : Convert.ToString(reader["CREATED_BY"]) ?? string.Empty,
            CreatedAt = reader["CREATED_AT"] == DBNull.Value ? null : Convert.ToDateTime(reader["CREATED_AT"]),
            UpdatedAt = reader["UPDATED_AT"] == DBNull.Value ? null : Convert.ToDateTime(reader["UPDATED_AT"]),
            IsActive = reader["IS_ACTIVE"] == DBNull.Value ? 1 : Convert.ToInt32(reader["IS_ACTIVE"])
        };
    }

    private static void AddParameter(System.Data.IDbCommand command, string name, object value)
    {
        System.Data.IDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static object NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static string GetMappingsJson(LayerDictionaryEntryDto entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.MappingsJson))
        {
            return entry.MappingsJson;
        }

        return JsonSerializer.Serialize(entry.Mappings ?? new List<LayerMappingDto>());
    }
}

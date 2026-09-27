using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using System.Diagnostics;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>
/// 系统配置服务。
/// 系统配置表只由服务器访问，客户端通过 API 读取和写入。
/// </summary>
public sealed class SystemConfigService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemConfigService> _logger;

    public SystemConfigService(IConfiguration configuration, ILogger<SystemConfigService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SystemConfigResponse> GetAsync(string key, CancellationToken cancellationToken)
    {
        string normalizedKey = NormalizeKey(key);
        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("系统配置读取开始。Key={Key}, DatabaseType={DatabaseType}, Schema={Schema}", normalizedKey, GetDatabaseType(), GetSchemaName());
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("系统配置读取数据库连接已打开。Key={Key}, ElapsedMs={ElapsedMs}", normalizedKey, stopwatch.ElapsedMilliseconds);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONFIG_VALUE FROM SYSTEM_CONFIG WHERE CONFIG_KEY = :ConfigKey";
        AddParameter(command, "ConfigKey", normalizedKey);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        string value = result == null || result == DBNull.Value ? string.Empty : Convert.ToString(result)?.Trim() ?? string.Empty;
        _logger.LogInformation("系统配置读取成功。Key={Key}", normalizedKey);
        return new SystemConfigResponse
        {
            Success = true,
            Key = normalizedKey,
            Value = value,
            Message = "系统配置读取成功。"
        };
    }

    public async Task<bool> CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("DM 健康检查开始。Schema={Schema}", GetSchemaName());
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM DUAL";
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("DM 健康检查成功。ElapsedMs={ElapsedMs}", stopwatch.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "健康检查中的 DM 数据库连接失败。");
            return false;
        }
    }

    public async Task<SystemConfigMutationResponse> SetAsync(string key, string? value, CancellationToken cancellationToken)
    {
        string normalizedKey = NormalizeKey(key);
        string normalizedValue = value ?? string.Empty;
        Stopwatch stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("系统配置写入开始。Key={Key}, ValueLength={ValueLength}, Schema={Schema}", normalizedKey, normalizedValue.Length, GetSchemaName());
        if (normalizedValue.Length > 4000)
            throw new ArgumentException("系统配置值长度不能超过 4000 个字符。", nameof(value));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"
MERGE INTO SYSTEM_CONFIG t
USING (SELECT :ConfigKey AS CONFIG_KEY, :ConfigValue AS CONFIG_VALUE FROM DUAL) s
ON (t.CONFIG_KEY = s.CONFIG_KEY)
WHEN MATCHED THEN UPDATE SET t.CONFIG_VALUE = s.CONFIG_VALUE
WHEN NOT MATCHED THEN INSERT (CONFIG_KEY, CONFIG_VALUE)
VALUES (s.CONFIG_KEY, s.CONFIG_VALUE)";
        AddParameter(command, "ConfigKey", normalizedKey);
        AddParameter(command, "ConfigValue", normalizedValue);
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("系统配置写入成功。Key={Key}, AffectedRows={AffectedRows}, ElapsedMs={ElapsedMs}", normalizedKey, affectedRows, stopwatch.ElapsedMilliseconds);
        return new SystemConfigMutationResponse
        {
            Success = true,
            AffectedRows = affectedRows,
            Message = "系统配置写入成功。"
        };
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

        _logger.LogInformation("DM 数据库连接开始。ServerConfigured={ServerConfigured}, Schema={Schema}", !string.IsNullOrWhiteSpace(connectionString), GetSchemaName());
        DmConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
            if (string.IsNullOrWhiteSpace(schema) || schema.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
                throw new InvalidOperationException("服务器数据库 Schema 配置无效。");

            using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = $"SET SCHEMA \"{schema.ToUpperInvariant()}\"";
            schemaCommand.ExecuteNonQuery();
            _logger.LogInformation("DM 数据库连接和 Schema 设置成功。Schema={Schema}", schema.ToUpperInvariant());
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant();

    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || schema.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            return "(invalid)";
        return schema.ToUpperInvariant();
    }

    private static string NormalizeKey(string key)
    {
        string normalizedKey = (key ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedKey))
            throw new ArgumentException("系统配置键不能为空。", nameof(key));
        if (normalizedKey.Length > 128)
            throw new ArgumentException("系统配置键长度不能超过 128 个字符。", nameof(key));
        return normalizedKey;
    }

    private static void AddParameter(System.Data.IDbCommand command, string name, object value)
    {
        System.Data.IDataParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

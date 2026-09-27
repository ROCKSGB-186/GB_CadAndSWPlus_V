using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>
/// 图层字典表初始化服务。
/// DM 连接只在服务器端创建，客户端不再为该初始化操作直接连接数据库。
/// </summary>
public sealed class LayerDictionaryInitializationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LayerDictionaryInitializationService> _logger;

    public LayerDictionaryInitializationService(
        IConfiguration configuration,
        ILogger<LayerDictionaryInitializationService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 幂等创建当前 DM Schema 下的 LAYER_DICTIONARY 表。
    /// </summary>
    public async Task<LayerDictionaryInitializationResponse> EnsureTableAsync(CancellationToken cancellationToken)
    {
        string connectionString = GetConnectionString();
        string schema = GetSchemaName();

        try
        {
            await using var connection = new DmConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // 连接进入目标 Schema，后续 USER_TABLES 查询和建表均作用于服务器配置的 Schema。
            using (var schemaCommand = connection.CreateCommand())
            {
                schemaCommand.CommandText = $"SET SCHEMA \"{schema.Replace("\"", "\"\"")}\"";
                schemaCommand.ExecuteNonQuery();
            }

            const string createTableSql = @"
DECLARE V_CNT INT;
BEGIN
  SELECT COUNT(*) INTO V_CNT FROM USER_TABLES WHERE TABLE_NAME = 'LAYER_DICTIONARY';
  IF V_CNT = 0 THEN
    EXECUTE IMMEDIATE 'CREATE TABLE LAYER_DICTIONARY (
      ID INT IDENTITY(1,1) PRIMARY KEY,
      SEQ INT DEFAULT 0,
      MAJOR VARCHAR(200),
      USERNAME VARCHAR(128),
      USER_ID INT,
      SOURCE VARCHAR(32) DEFAULT ''''personal'''' ,
      MAPPINGS_JSON TEXT,
      CREATED_BY VARCHAR(128),
      CREATED_AT DATETIME DEFAULT CURRENT_TIMESTAMP,
      UPDATED_AT DATETIME,
      IS_ACTIVE TINYINT DEFAULT 1
    )';
  END IF;
END;";

            using (var command = connection.CreateCommand())
            {
                command.CommandText = createTableSql;
                command.ExecuteNonQuery();
            }

            _logger.LogInformation("图层字典表初始化成功。Schema={Schema}", schema);
            return new LayerDictionaryInitializationResponse
            {
                Success = true,
                Message = "图层字典表已就绪。"
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 日志只记录 Schema 和异常，不记录连接字符串、密码等敏感信息。
            _logger.LogError(ex, "图层字典表初始化失败。Schema={Schema}", schema);
            return new LayerDictionaryInitializationResponse
            {
                Success = false,
                Message = "图层字典表初始化失败，请查看服务器日志。"
            };
        }
    }

    private string GetConnectionString()
    {
        string connectionString = (_configuration["Database:ConnectionString"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string dmConnectionString = (_configuration.GetConnectionString("DM") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(dmConnectionString))
            throw new InvalidOperationException("服务器未配置达梦数据库连接字符串。");

        return dmConnectionString;
    }

    private string GetSchemaName()
    {
        string schema = (_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim();
        if (string.IsNullOrWhiteSpace(schema) || schema.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException("服务器数据库 Schema 配置无效。");
        }

        return schema.ToUpperInvariant();
    }
}

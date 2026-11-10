using System.Security.Cryptography;
using GB_CadAndSWPlus_V.UploadApi.Models;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>属性定义新增冲突，不包含数据库内部信息。</summary>
public sealed class ElementPropertyDefinitionConflictException : Exception
{
    public ElementPropertyDefinitionConflictException(string message) : base(message) { }
}

public sealed class ElementPropertyElementNotFoundException : Exception
{
    public ElementPropertyElementNotFoundException() : base("构件不存在。") { }
}

public sealed class ElementPropertyDefinitionDatabaseException : Exception
{
    public ElementPropertyDefinitionDatabaseException() : base("数据库操作失败，请联系管理员。") { }
}

public sealed class ElementPropertyDefinitionValidationException : ArgumentException
{
    public ElementPropertyDefinitionValidationException(string message) : base(message) { }
}

/// <summary>隔离事务边界，便于不连接数据库验证全批次行为。</summary>
public interface IElementPropertyDefinitionTransaction : IAsyncDisposable
{
    Task<string?> LockElementAsync(long elementId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ElementPropertyDto>> ListDefinitionsAsync(long elementId, CancellationToken cancellationToken);
    Task InsertAsync(ElementPropertyDto definition, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

public static class ElementPropertyDefinitionCreation
{
    public const int MaximumBatchSize = 256;

    public static IReadOnlyList<ElementPropertyDto> Validate(long elementId, IReadOnlyList<CreateElementPropertyDefinitionRequest?>? requests)
    {
        if (elementId <= 0) throw new ElementPropertyDefinitionValidationException("构件 ID 必须大于 0。");
        if (requests == null || requests.Count == 0 || requests.Count > MaximumBatchSize)
            throw new ElementPropertyDefinitionValidationException($"属性定义数量必须为 1 至 {MaximumBatchSize} 项。");

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ElementPropertyDto>(requests.Count);
        foreach (var request in requests)
        {
            if (request == null) throw new ElementPropertyDefinitionValidationException("属性定义不能为 null。");
            string code = Required(request.Code, 128, "属性编码");
            string name = Required(request.Name, 255, "属性名称");
            string type = Required(request.DataType, 32, "数据类型").ToUpperInvariant();
            if (type != "TEXT")
                throw new ElementPropertyDefinitionValidationException("当前仅支持 TEXT 属性；数值和布尔属性尚未支持完整保存。");
            string? unit = request.Unit?.Trim();
            if (unit?.Length > 64 || (request.Unit != null && request.Unit.Any(char.IsControl)))
                throw new ElementPropertyDefinitionValidationException("单位不能包含控制字符或超过 64 个字符。");
            if (!request.IsRequired.HasValue || !request.SortOrder.HasValue)
                throw new ElementPropertyDefinitionValidationException("必须提供 isRequired 和 sortOrder。");
            if (!codes.Add(code)) throw new ElementPropertyDefinitionValidationException("同一批次属性编码不能重复。");
            if (!names.Add(name)) throw new ElementPropertyDefinitionValidationException("同一批次属性名称不能重复。");
            result.Add(new ElementPropertyDto
            {
                ElementDefinitionId = elementId, PropertyCode = code, PropertyName = name,
                DataType = type, Unit = unit ?? string.Empty,
                IsRequired = request.IsRequired.Value, SortOrder = request.SortOrder.Value
            });
        }
        return result;
    }

    public static string ValidateConnectionStrings(string? typedConnectionString, string? commonConnectionString)
    {
        // 构件服务优先使用类型专用连接，现有属性读写使用通用连接；不一致时禁止新增。
        string elementConnection = (typedConnectionString ?? commonConnectionString ?? string.Empty).Trim();
        string metadataConnection = (commonConnectionString ?? string.Empty).Trim();
        if (elementConnection.Length == 0 || !string.Equals(elementConnection, metadataConnection, StringComparison.Ordinal))
            throw new ElementPropertyDefinitionDatabaseException();
        return metadataConnection;
    }

    private static string Required(string? value, int maxLength, string label)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength || (value != null && value.Any(char.IsControl)))
            throw new ElementPropertyDefinitionValidationException($"{label}不能为空、包含控制字符或超过 {maxLength} 个字符。");
        return normalized;
    }

    public static async Task<IReadOnlyList<ElementPropertyDto>> ExecuteAsync(
        long elementId, IReadOnlyList<ElementPropertyDto> definitions,
        IElementPropertyDefinitionTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteTransactionAsync(elementId, definitions, transaction, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 即使请求取消也尝试回滚；回滚异常不能被静默忽略。
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IReadOnlyList<ElementPropertyDto>> ExecuteTransactionAsync(
        long elementId, IReadOnlyList<ElementPropertyDto> definitions,
        IElementPropertyDefinitionTransaction transaction, CancellationToken cancellationToken)
    {
        // 锁定构件行，串行化本接口的同构件写入并阻止并发归档。
        string? status = await transaction.LockElementAsync(elementId, cancellationToken).ConfigureAwait(false);
        if (status == null) throw new ElementPropertyElementNotFoundException();
        if (!new[] { "DRAFT", "REVIEW", "PUBLISHED" }.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            throw new ElementPropertyDefinitionConflictException("构件已归档、失效或状态无效，不能新增属性。");

        var existing = await transaction.ListDefinitionsAsync(elementId, cancellationToken).ConfigureAwait(false);
        var codes = new HashSet<string>(existing.Select(x => x.PropertyCode.Trim()), StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(existing.Select(x => x.PropertyName.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
            if (!codes.Add(definition.PropertyCode) || !names.Add(definition.PropertyName))
                throw new ElementPropertyDefinitionConflictException("构件已存在相同编码或名称的属性定义。");

        // 随机正整数 ID 在本批中去重；数据库主键仍是最终冲突保障。
        var ids = new HashSet<long>();
        var created = new List<ElementPropertyDto>(definitions.Count);
        foreach (var definition in definitions)
        {
            long id;
            do { id = RandomNumberGenerator.GetInt32(1, 1 << 22) * (1L << 31) + RandomNumberGenerator.GetInt32(int.MaxValue); }
            while (!ids.Add(id));
            var item = new ElementPropertyDto
            {
                Id = id, ElementDefinitionId = elementId, PropertyCode = definition.PropertyCode,
                PropertyName = definition.PropertyName, DataType = definition.DataType, Unit = definition.Unit,
                IsRequired = definition.IsRequired, SortOrder = definition.SortOrder
            };
            await transaction.InsertAsync(item, cancellationToken).ConfigureAwait(false);
            created.Add(item);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }
}

using Dapper;
using Dm;
using GB_CadAndSWPlus_V.UploadApi.Models;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using System.Data.Common;
using System.Security.Cryptography;

namespace GB_CadAndSWPlus_V.UploadApi.Services;

/// <summary>SolidWorks 模型及预览附件的受控存储服务。</summary>
public sealed class SolidWorksResourceService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SolidWorksResourceService> _logger;
    private readonly ElementResourceService _resourceService;

    public SolidWorksResourceService(IConfiguration configuration, ILogger<SolidWorksResourceService> logger, ElementResourceService resourceService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceService = resourceService ?? throw new ArgumentNullException(nameof(resourceService));
    }

    public async Task<ElementResourceResponse> UploadModelAsync(long elementId, IFormFile file, string? resourceName, string? configurationName, string operatorName, CancellationToken cancellationToken)
    {
        if (elementId <= 0) throw new ArgumentOutOfRangeException(nameof(elementId), "构件 ID 必须大于 0。");
        if (file == null || file.Length == 0) throw new ArgumentException("SolidWorks 模型文件不能为空。", nameof(file));
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".sldprt" && extension != ".sldasm") throw new ArgumentException("仅允许上传 SLDPRT 或 SLDASM 文件。", nameof(file));
        if (file.Length > 1024L * 1024L * 1024L) throw new ArgumentException("SolidWorks 模型文件不能超过 1 GB。", nameof(file));

        string relativePath = Path.Combine("SOLIDWORKS", elementId.ToString(), Guid.NewGuid().ToString("N") + extension);
        string fullPath = GetSafePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string hash;
        await using (FileStream output = File.Create(fullPath))
        {
            await file.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        await using (FileStream input = File.OpenRead(fullPath))
        using (SHA256 sha = SHA256.Create())
            hash = Convert.ToHexString(await sha.ComputeHashAsync(input, cancellationToken).ConfigureAwait(false));

        ElementResourceResponse response = await _resourceService.CreateAsync(new ElementResourceRequest
        {
            ElementDefinitionId = elementId,
            Platform = "SOLIDWORKS",
            ResourceType = extension == ".sldasm" ? "SLDASM" : "SLDPRT",
            ResourceName = string.IsNullOrWhiteSpace(resourceName) ? Path.GetFileNameWithoutExtension(file.FileName) : resourceName,
            ResourcePath = relativePath,
            FileHash = hash,
            ConfigurationName = configurationName,
            Status = "DRAFT",
            IsDefault = true
        }, operatorName, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SolidWorks 模型上传成功。ElementId={ElementId}, ResourceId={ResourceId}, FileSize={FileSize}", elementId, response.Resource?.Id, file.Length);
        return response;
    }

    /// <summary>打开已绑定的 SolidWorks 模型文件；调用方通过资源 ID 控制访问范围。</summary>
    public async Task<SolidWorksModelFile?> OpenModelAsync(long resourceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始读取 SolidWorks 模型文件。ResourceId={ResourceId}", resourceId);
        if (resourceId <= 0) throw new ArgumentOutOfRangeException(nameof(resourceId), "资源 ID 必须大于 0。");
        ElementResourceResponse resource = await _resourceService.GetAsync(resourceId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SolidWorks 模型资源元数据查询完成。ResourceId={ResourceId}, Success={Success}, HasResource={HasResource}", resourceId, resource.Success, resource.Resource != null);
        if (!resource.Success || resource.Resource == null || !string.Equals(resource.Resource.Platform, "SOLIDWORKS", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("SolidWorks 模型资源不存在或平台不匹配。ResourceId={ResourceId}", resourceId);
            return null;
        }

        string fullPath = GetSafePath(resource.Resource.ResourcePath);
        _logger.LogInformation("SolidWorks 模型受控路径解析完成。ResourceId={ResourceId}, FileName={FileName}", resourceId, Path.GetFileName(fullPath));
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("SolidWorks 模型物理文件不存在。ResourceId={ResourceId}", resourceId);
            return null;
        }
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension != ".sldprt" && extension != ".sldasm")
        {
            _logger.LogWarning("SolidWorks 模型文件扩展名不受支持。ResourceId={ResourceId}, Extension={Extension}", resourceId, extension);
            return null;
        }
        string fileName = string.IsNullOrWhiteSpace(resource.Resource.ResourceName)
            ? Path.GetFileName(fullPath)
            : resource.Resource.ResourceName + extension;
        _logger.LogInformation("SolidWorks 模型文件读取成功。ResourceId={ResourceId}, FileName={FileName}, FileSize={FileSize}", resourceId, fileName, new FileInfo(fullPath).Length);
        FileStream content = File.OpenRead(fullPath);
        _logger.LogInformation("SolidWorks 模型文件流已打开，准备交付响应。ResourceId={ResourceId}, FileSize={FileSize}", resourceId, content.Length);
        return new SolidWorksModelFile(content, fileName, extension == ".sldasm" ? "application/x-sldasm" : "application/x-sldprt");
    }

    /// <summary>替换现有 SolidWorks 资源的模型文件，保持资源 ID 不变。</summary>
    public async Task<ElementResourceResponse> ReplaceModelAsync(long resourceId, IFormFile file, string? resourceName, string? configurationName, string operatorName, CancellationToken cancellationToken)
    {
        if (resourceId <= 0) throw new ArgumentOutOfRangeException(nameof(resourceId), "资源 ID 必须大于 0。");
        if (file == null || file.Length == 0) throw new ArgumentException("SolidWorks 模型文件不能为空。", nameof(file));
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".sldprt" && extension != ".sldasm") throw new ArgumentException("仅允许上传 SLDPRT 或 SLDASM 文件。", nameof(file));
        if (file.Length > 1024L * 1024L * 1024L) throw new ArgumentException("SolidWorks 模型文件不能超过 1 GB。", nameof(file));

        ElementResourceResponse current = await _resourceService.GetAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (!current.Success || current.Resource == null || current.Resource.Platform != "SOLIDWORKS")
            return new ElementResourceResponse { Success = false, Message = "SolidWorks 模型资源不存在。" };

        string relativePath = Path.Combine("SOLIDWORKS", current.Resource.ElementDefinitionId.ToString(), Guid.NewGuid().ToString("N") + extension);
        string fullPath = GetSafePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (FileStream output = File.Create(fullPath))
            await file.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        string hash;
        await using (FileStream input = File.OpenRead(fullPath))
        using (SHA256 sha = SHA256.Create())
            hash = Convert.ToHexString(await sha.ComputeHashAsync(input, cancellationToken).ConfigureAwait(false));

        ElementResourceResponse response = await _resourceService.UpdateAsync(resourceId, new ElementResourceRequest
        {
            ElementDefinitionId = current.Resource.ElementDefinitionId,
            Platform = "SOLIDWORKS",
            ResourceType = extension == ".sldasm" ? "SLDASM" : "SLDPRT",
            ResourceName = string.IsNullOrWhiteSpace(resourceName) ? Path.GetFileNameWithoutExtension(file.FileName) : resourceName,
            ResourcePath = relativePath,
            FileHash = hash,
            ConfigurationName = configurationName,
            Status = "DRAFT",
            IsDefault = current.Resource.IsDefault
        }, operatorName, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SolidWorks 模型替换成功。ResourceId={ResourceId}, FileSize={FileSize}", resourceId, file.Length);
        return response;
    }

    public sealed record SolidWorksModelFile(Stream Content, string FileName, string ContentType);

    public async Task<SolidWorksResourceAttachmentResponse> UploadAttachmentAsync(long resourceId, IFormFile file, string attachmentType, string operatorName, CancellationToken cancellationToken)
    {
        if (resourceId <= 0) throw new ArgumentOutOfRangeException(nameof(resourceId), "资源 ID 必须大于 0。");
        if (file == null || file.Length == 0) throw new ArgumentException("预览文件不能为空。", nameof(file));
        string type = (attachmentType ?? string.Empty).Trim().ToUpperInvariant();
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (type == "PREVIEW_IMAGE" && extension != ".png" && extension != ".jpg" && extension != ".jpeg") throw new ArgumentException("图片预览仅允许 PNG、JPG 或 JPEG。", nameof(file));
        if (type == "PREVIEW_3D" && extension != ".glb" && extension != ".gltf") throw new ArgumentException("三维预览仅允许 GLB 或 GLTF。", nameof(file));
        if (type != "PREVIEW_IMAGE" && type != "PREVIEW_3D") throw new ArgumentException("附件类型必须是 PREVIEW_IMAGE 或 PREVIEW_3D。", nameof(attachmentType));
        if (file.Length > 100L * 1024L * 1024L) throw new ArgumentException("预览附件不能超过 100 MB。", nameof(file));

        string relativePath = Path.Combine("SOLIDWORKS", "PREVIEWS", resourceId.ToString(), Guid.NewGuid().ToString("N") + extension);
        string fullPath = GetSafePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (FileStream output = File.Create(fullPath)) await file.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        string hash;
        await using (FileStream input = File.OpenRead(fullPath)) using (SHA256 sha = SHA256.Create()) hash = Convert.ToHexString(await sha.ComputeHashAsync(input, cancellationToken).ConfigureAwait(false));

        string typeName = GetDatabaseType();
        string prefix = typeName == "MYSQL" ? "@" : ":";
        long id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(1, 999);
        DateTime now = DateTime.Now;
        await using DbConnection connection = CreateConnection(typeName);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        string table = AttachmentTable(typeName);
        string sql = $@"UPDATE {table} SET status='ARCHIVED', updated_at={prefix}Now WHERE resource_id={prefix}ResourceId AND attachment_type={prefix}AttachmentType AND status<>'ARCHIVED';
INSERT INTO {table} (id, resource_id, attachment_type, original_file_name, stored_file_name, mime_type, file_path, file_hash, file_size, version, status, created_by, updated_by, created_at, updated_at)
VALUES ({prefix}Id, {prefix}ResourceId, {prefix}AttachmentType, {prefix}OriginalFileName, {prefix}StoredFileName, {prefix}MimeType, {prefix}FilePath, {prefix}FileHash, {prefix}FileSize, 1, 'PUBLISHED', {prefix}OperatorName, {prefix}OperatorName, {prefix}Now, {prefix}Now)";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, ResourceId = resourceId, AttachmentType = type, OriginalFileName = Path.GetFileName(file.FileName), StoredFileName = Path.GetFileName(relativePath), MimeType = file.ContentType ?? "application/octet-stream", FilePath = relativePath, FileHash = hash, FileSize = file.Length, OperatorName = operatorName, Now = now }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        _logger.LogInformation("SolidWorks 预览附件上传成功。ResourceId={ResourceId}, AttachmentType={AttachmentType}, FileSize={FileSize}", resourceId, type, file.Length);
        return new SolidWorksResourceAttachmentResponse { Success = true, Message = "预览附件上传成功。", Attachment = new SolidWorksResourceAttachmentDto { Id = id, ResourceId = resourceId, AttachmentType = type, OriginalFileName = Path.GetFileName(file.FileName), MimeType = file.ContentType ?? "application/octet-stream", FileSize = file.Length, Version = 1, Status = "PUBLISHED", CreatedAt = now, UpdatedAt = now } };
    }

    public async Task<bool> ArchiveAttachmentAsync(long attachmentId, CancellationToken cancellationToken)
    {
        if (attachmentId <= 0) throw new ArgumentOutOfRangeException(nameof(attachmentId));
        string type = GetDatabaseType(); string prefix = type == "MYSQL" ? "@" : ":";
        await using DbConnection connection = CreateConnection(type); await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        int count = await connection.ExecuteAsync(new CommandDefinition($"UPDATE {AttachmentTable(type)} SET status='ARCHIVED', updated_at={prefix}Now WHERE id={prefix}Id", new { Id = attachmentId, Now = DateTime.Now }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return count > 0;
    }

    /// <summary>查询指定资源当前有效的预览附件。</summary>
    public async Task<IReadOnlyList<SolidWorksResourceAttachmentDto>> ListAttachmentsAsync(long resourceId, CancellationToken cancellationToken)
    {
        if (resourceId <= 0) throw new ArgumentOutOfRangeException(nameof(resourceId));
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        string sql = $@"SELECT id AS Id, resource_id AS ResourceId, attachment_type AS AttachmentType,
original_file_name AS OriginalFileName, mime_type AS MimeType, file_size AS FileSize,
version AS Version, status AS Status, created_at AS CreatedAt, updated_at AS UpdatedAt
FROM {AttachmentTable(type)}
WHERE resource_id={prefix}ResourceId AND status<>'ARCHIVED'
ORDER BY attachment_type, version DESC, updated_at DESC";
        return (await connection.QueryAsync<SolidWorksResourceAttachmentDto>(new CommandDefinition(sql, new { ResourceId = resourceId }, cancellationToken: cancellationToken)).ConfigureAwait(false)).AsList();
    }

    /// <summary>按附件编号打开受控预览文件流。</summary>
    public async Task<SolidWorksAttachmentFile?> OpenAttachmentAsync(long attachmentId, CancellationToken cancellationToken)
    {
        if (attachmentId <= 0) throw new ArgumentOutOfRangeException(nameof(attachmentId));
        string type = GetDatabaseType();
        string prefix = type == "MYSQL" ? "@" : ":";
        await using DbConnection connection = CreateConnection(type);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        SolidWorksAttachmentRow? row = await connection.QuerySingleOrDefaultAsync<SolidWorksAttachmentRow>(new CommandDefinition(
            $"SELECT original_file_name AS OriginalFileName, mime_type AS MimeType, file_path AS FilePath FROM {AttachmentTable(type)} WHERE id={prefix}Id AND status<>'ARCHIVED'",
            new { Id = attachmentId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (row == null) return null;
        string fullPath = GetSafePath(row.FilePath);
        if (!File.Exists(fullPath)) return null;
        return new SolidWorksAttachmentFile
        {
            Content = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read),
            FileName = Path.GetFileName(row.OriginalFileName),
            ContentType = string.IsNullOrWhiteSpace(row.MimeType) ? "application/octet-stream" : row.MimeType
        };
    }

    private sealed class SolidWorksAttachmentRow
    {
        public string OriginalFileName { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    public sealed class SolidWorksAttachmentFile
    {
        public Stream Content { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string ContentType { get; init; } = "application/octet-stream";
    }

    private string GetSafePath(string relativePath)
    {
        string root = (_configuration["Storage:Root"] ?? _configuration["StorageSettings:RootPath"] ?? _configuration["UploadStorage:RootPath"] ?? Path.Combine(AppContext.BaseDirectory, "StandardFiles")).Trim();
        string rootFull = Path.GetFullPath(root); string full = Path.GetFullPath(Path.Combine(rootFull, relativePath));
        if (!full.StartsWith(rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("文件路径不在服务器受控目录内。");
        return full;
    }
    private string GetDatabaseType() => (_configuration["Database:Type"] ?? "DM").Trim().ToUpperInvariant() == "MYSQL" ? "MYSQL" : "DM";
    private DbConnection CreateConnection(string type) => type == "MYSQL" ? new MySqlConnection(GetConnectionString("MYSQL")) : new DmConnection(GetConnectionString("DM"));
    private string GetConnectionString(string type) => (_configuration[$"ConnectionStrings:{(type == "MYSQL" ? "MySQL" : "DM")}" ] ?? _configuration["Database:ConnectionString"] ?? string.Empty).Trim();
    private string AttachmentTable(string type) => type == "MYSQL" ? "element_resource_attachments" : $"{(_configuration["Database:Schema"] ?? "CAD_SW_LIBRARY").Trim().ToUpperInvariant()}.ELEMENT_RESOURCE_ATTACHMENTS";
}

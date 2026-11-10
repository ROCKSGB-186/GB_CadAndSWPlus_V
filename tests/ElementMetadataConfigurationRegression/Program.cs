global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.Logging;
using GB_CadAndSWPlus_V.UploadApi.Models;
using GB_CadAndSWPlus_V.UploadApi.Services;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }

    private static void RejectConnections(string? typed, string? common)
    {
        try { ElementPropertyDefinitionCreation.ValidateConnectionStrings(typed, common); }
        catch (ElementPropertyDefinitionDatabaseException ex)
        {
            Check(!ex.Message.Contains("synthetic", StringComparison.OrdinalIgnoreCase), "错误消息不得包含连接信息");
            return;
        }
        throw new Exception("不一致或缺失的连接配置必须拒绝");
    }

    private static async Task Main()
    {
        string a = "synthetic-A-" + Guid.NewGuid();
        string b = "synthetic-B-" + Guid.NewGuid();
        Check(ElementPropertyDefinitionCreation.ValidateConnectionStrings(a, a) == a, "相同连接必须通过");
        Check(ElementPropertyDefinitionCreation.ValidateConnectionStrings(null, a) == a, "未配置专用连接时必须回退通用连接");
        Check(ElementPropertyDefinitionCreation.ValidateConnectionStrings("  " + a + " ", "\t" + a + "\r\n") == a, "仅去除首尾空白后比较");
        foreach (var pair in new (string? Typed, string? Common)[]
        {
            (null, null), (null, ""), (null, " "), ("", a), (" ", a), (a, null), (a, ""),
            (a, b), (a.ToUpperInvariant(), a), (a + ";x=1", a), (a + " ", a + "x")
        })
            RejectConnections(pair.Typed, pair.Common);

        var text = ElementPropertyDefinitionCreation.Validate(1, new[] {
            new CreateElementPropertyDefinitionRequest { Code = "DN", Name = "通径", DataType = " text ", IsRequired = true, SortOrder = 1 }
        });
        Check(text.Count == 1 && text[0].DataType == "TEXT", "仅支持 TEXT 并标准化大小写");
        foreach (string type in new[] { "NUMBER", "number", "BOOLEAN", "boolean", "INTEGER", "" })
        {
            try
            {
                ElementPropertyDefinitionCreation.Validate(1, new[] {
                    new CreateElementPropertyDefinitionRequest { Code = "X", Name = "属性", DataType = type, IsRequired = false, SortOrder = 1 }
                });
            }
            catch (ElementPropertyDefinitionValidationException) { checks++; continue; }
            throw new Exception("不支持的属性类型必须拒绝");
        }

        foreach (string type in new[] { "MYSQL", "DM" })
        {
            await CheckService(type, a, b, null, false);
            await CheckService(type, a, "", null, false);
            await CheckService(type, null, a, null, false);
            await CheckService(type, null, null, null, false);
            await CheckService(type, a, a, b, true);
            await CheckService(type, a, null, b, true);
            await CheckUnsupportedBatch(type, a, "NUMBER");
            await CheckUnsupportedBatch(type, a, "BOOLEAN");
        }
        Console.WriteLine($"连接一致性和 TEXT 回归通过：{checks} 项检查。");
    }

    private static async Task CheckService(string type, string? common, string? typed, string? otherTyped, bool shouldReachFakeProvider)
    {
        string target = type == "MYSQL" ? "MySQL" : "DM";
        string other = type == "MYSQL" ? "DM" : "MySQL";
        var settings = new Dictionary<string, string?>
        {
            ["Database:Type"] = type,
            ["Database:ConnectionString"] = common,
            ["ConnectionStrings:" + target] = typed,
            ["ConnectionStrings:" + other] = otherTyped
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var logger = new RecordingLogger<ElementMetadataService>();
        var service = new ElementMetadataService(configuration, logger);
        FakeDatabase.ProviderConstructed = 0;
        try
        {
            await service.CreateDefinitionsAsync(1, new[] {
                new CreateElementPropertyDefinitionRequest { Code = "DN", Name = "通径", DataType = "TEXT", IsRequired = true, SortOrder = 1 }
            }, "admin", default);
            throw new Exception("模拟驱动绝不允许写入");
        }
        catch (ElementPropertyDefinitionDatabaseException ex) when (!shouldReachFakeProvider)
        {
            Check(FakeDatabase.ProviderConstructed == 0 && logger.Messages.Any(x => x.Contains("数据库连接配置不一致或缺失")), "配置不匹配必须在创建连接前拒绝");
            Check((common == null || !ex.Message.Contains(common)) && (string.IsNullOrEmpty(typed) || !ex.Message.Contains(typed)),
                "错误响应不得包含连接信息");
            Check(logger.Messages.All(x => (common == null || !x.Contains(common)) && (typed == null || typed.Length == 0 || !x.Contains(typed))),
                "日志不得包含连接信息");
        }
        catch (FakeConnectionReachedException) when (shouldReachFakeProvider)
        {
            Check(FakeDatabase.ProviderConstructed == 1, "配置一致后应进入模拟驱动，不接触真实数据库");
        }
    }

    private static async Task CheckUnsupportedBatch(string type, string connection, string unsupportedType)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Type"] = type,
            ["Database:ConnectionString"] = connection
        };
        var service = new ElementMetadataService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new RecordingLogger<ElementMetadataService>());
        FakeDatabase.ProviderConstructed = 0;
        try
        {
            await service.CreateDefinitionsAsync(1, new[] {
                new CreateElementPropertyDefinitionRequest { Code = "DN", Name = "通径", DataType = "TEXT", IsRequired = true, SortOrder = 1 },
                new CreateElementPropertyDefinitionRequest { Code = "PN", Name = "压力", DataType = unsupportedType, IsRequired = false, SortOrder = 2 }
            }, "admin", default);
        }
        catch (ElementPropertyDefinitionValidationException)
        {
            Check(FakeDatabase.ProviderConstructed == 0, "混合批次包含不支持类型时必须在打开连接前全部拒绝");
            return;
        }
        throw new Exception("不支持的属性类型必须拒绝");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public readonly List<string> Messages = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}

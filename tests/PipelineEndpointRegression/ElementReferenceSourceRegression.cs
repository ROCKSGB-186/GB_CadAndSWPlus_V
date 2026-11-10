using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GB_CadAndSWPlus_V.SolidWorksAddIn.Services;
using Api = GB_CadAndSWPlus_V.SolidWorksAddIn.Services.SolidWorksResourceApiService;

internal static class ElementReferenceSourceRegression
{
    private static int _count;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _count++;
    }

    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { _count++; return; }
        throw new InvalidOperationException(message);
    }

    private static Api.PropertyValueDto Property(string code, string? text = null, decimal? number = null,
        bool? boolean = null, string? unit = null, long id = 42) => new Api.PropertyValueDto
    {
        ElementDefinitionId = id, PropertyCode = code, PropertyName = code + "名称",
        ValueText = text ?? string.Empty, ValueNumber = number, ValueBool = boolean, Unit = unit ?? string.Empty
    };

    public static int Run()
    {
        _count = 0;
        var api = new Api();
        var source = new PipelineElementReferenceSource(api);
        string? actualKeyword = null;
        api.Search = (keyword, token) =>
        {
            actualKeyword = keyword;
            return Task.FromResult(new Api.ElementListResponse { Success = true, Elements = new List<Api.ElementDto>
            {
                new Api.ElementDto { Id = 42, ElementCode = "V-42", Name = "原名称", DisplayName = "蝶阀", Status = "ACTIVE" },
                new Api.ElementDto { Id = 43, ElementCode = "V-43", Name = "法兰" }
            }});
        };
        var elements = source.SearchAsync("阀门", CancellationToken.None).GetAwaiter().GetResult();
        Check(actualKeyword == "阀门" && elements.Count == 2 && elements[0].Id == 42 && elements[0].Name == "蝶阀" &&
              elements[1].Name == "法兰" && elements[1].Status == "未知", "搜索结果应保留身份并使用显示名称及状态回退");

        long actualId = 0;
        api.List = (id, token) =>
        {
            actualId = id;
            return Task.FromResult(new Api.PropertyListResponse { Success = true, Data = new List<Api.PropertyValueDto>
            {
                Property(" DN ", " DN100 ", unit: " mm "),
                Property("PN", number: 1.25m, unit: " MPa "),
                Property("EMPTY", "  "),
                Property("BOOL", boolean: false),
                new Api.PropertyValueDto { ElementDefinitionId = 42, PropertyCode = "BOOLTEXT", DataType = "boolean", ValueText = "true" },
                Property("MULTI", "文字", number: 2),
                Property("MIXED_BOOL", "文字", boolean: true),
                Property("CONTROL", "含\n控制符")
            }});
        };
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            // 模拟逗号小数点环境，确保数值不会依赖用户区域设置。
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var candidates = source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult();
            Check(actualId == 42 && candidates.Count == 2, "空值、布尔值、多值及控制字符必须排除");
            Check(candidates[0].Code == "DN" && candidates[0].Value == "DN100" && candidates[0].Unit == "mm" &&
                  candidates[0].Label.Contains("单位：mm"), "文本值和原始单位应保留并去除边缘空格");
            Check(candidates[1].Value == "1.25" && candidates[1].Unit == "MPa" && candidates[1].Label.Contains("单位：MPa"),
                "数字值应使用不变区域格式并保持原单位，不转换数值");
        }
        finally { Thread.CurrentThread.CurrentCulture = previousCulture; }

        void Properties(params Api.PropertyValueDto[] items) => api.List = (id, token) =>
            Task.FromResult(new Api.PropertyListResponse { Success = true, Data = items.ToList() });
        Properties(
            new Api.PropertyValueDto { ElementDefinitionId = 42, PropertyCode = "A", DataType = "BOOL", ValueText = "1" },
            new Api.PropertyValueDto { ElementDefinitionId = 42, PropertyCode = "B", DataType = " boolean ", ValueText = "false" });
        Check(source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult().Count == 0,
            "BOOL/BOOLEAN 类型即使以文本保存也不得作为候选");
        Properties(Property("DN", "DN100", id: 43));
        Reject<PipelineReferenceException>(() => source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "错误构件 ID 必须拒绝整批属性");
        Properties(Property("DN", "a"), Property(" dn ", "b"));
        Reject<PipelineReferenceException>(() => source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "大小写及空格不同的重复字段必须拒绝");
        Reject<ArgumentOutOfRangeException>(() => source.GetPropertiesAsync(0, CancellationToken.None).GetAwaiter().GetResult(), "无效请求 ID 必须拒绝");

        var many = Enumerable.Range(1, 201).Select(i => new Api.ElementDto { Id = i, ElementCode = "E" + i, Name = "构件" }).ToList();
        api.Search = (keyword, token) => Task.FromResult(new Api.ElementListResponse { Success = true, Elements = many });
        Reject<PipelineReferenceException>(() => source.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "超过 200 个搜索结果必须拒绝");
        many.RemoveAt(200);
        Check(source.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult().Count == 200, "恰好 200 个有效结果应允许");
        many[1].Id = many[0].Id;
        Reject<PipelineReferenceException>(() => source.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "重复构件 ID 必须拒绝");

        api.Search = (keyword, token) => Task.FromResult(new Api.ElementListResponse { Success = false, Elements = new List<Api.ElementDto>() });
        api.List = (id, token) => Task.FromResult(new Api.PropertyListResponse { Success = false, Data = new List<Api.PropertyValueDto>() });
        Reject<PipelineReferenceException>(() => source.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "搜索失败不得返回成功");
        Reject<PipelineReferenceException>(() => source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "属性请求失败不得返回成功");
        api.Search = (keyword, token) => Task.FromResult(new Api.ElementListResponse { Success = true, Elements = null });
        api.List = (id, token) => Task.FromResult(new Api.PropertyListResponse { Success = true, Data = null });
        Reject<PipelineReferenceException>(() => source.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "搜索空数据不得返回成功");
        Reject<PipelineReferenceException>(() => source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "属性空数据不得返回成功");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            api.Search = (keyword, token) => Task.FromCanceled<Api.ElementListResponse>(token);
            api.List = (id, token) => Task.FromCanceled<Api.PropertyListResponse>(token);
            Reject<OperationCanceledException>(() => source.SearchAsync("构件", cancellation.Token).GetAwaiter().GetResult(), "搜索取消必须传播取消");
            Reject<OperationCanceledException>(() => source.GetPropertiesAsync(42, cancellation.Token).GetAwaiter().GetResult(), "属性取消必须传播取消");
        }
        api.Search = (keyword, token) => Task.FromException<Api.ElementListResponse>(new OperationCanceledException());
        api.List = (id, token) => Task.FromException<Api.PropertyListResponse>(new OperationCanceledException());
        Reject<PipelineReferenceException>(() => source.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "非调用方取消的搜索必须报告超时");
        Reject<PipelineReferenceException>(() => source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "非调用方取消的属性请求必须报告超时");
        api.Search = (keyword, token) => Task.FromException<Api.ElementListResponse>(new InvalidOperationException("模拟错误"));
        api.List = (id, token) => Task.FromException<Api.PropertyListResponse>(new InvalidOperationException("模拟错误"));
        Reject<InvalidOperationException>(() => source.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "搜索异常不得返回成功");
        Reject<InvalidOperationException>(() => source.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "属性异常不得返回成功");

        bool current = true;
        var guarded = new PipelineElementReferenceSource(api, () => current);
        current = false;
        int calls = 0;
        api.Search = (keyword, token) => { calls++; return Task.FromResult(new Api.ElementListResponse { Success = true, Elements = new List<Api.ElementDto>() }); };
        api.List = (id, token) => { calls++; return Task.FromResult(new Api.PropertyListResponse { Success = true, Data = new List<Api.PropertyValueDto>() }); };
        Reject<PipelineReferenceException>(() => guarded.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "失效会话不得开始搜索");
        Reject<PipelineReferenceException>(() => guarded.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "失效会话不得开始属性读取");
        Check(calls == 0, "失效会话不得调用 API 替身");
        current = true;
        api.Search = (keyword, token) => { current = false; return Task.FromResult(new Api.ElementListResponse { Success = true, Elements = new List<Api.ElementDto>() }); };
        Reject<PipelineReferenceException>(() => guarded.SearchAsync("构件", CancellationToken.None).GetAwaiter().GetResult(), "搜索期间会话切换不得返回结果");
        current = true;
        api.List = (id, token) => { current = false; return Task.FromResult(new Api.PropertyListResponse { Success = true, Data = new List<Api.PropertyValueDto>() }); };
        Reject<PipelineReferenceException>(() => guarded.GetPropertiesAsync(42, CancellationToken.None).GetAwaiter().GetResult(), "属性读取期间会话切换不得返回结果");
        return _count;
    }
}

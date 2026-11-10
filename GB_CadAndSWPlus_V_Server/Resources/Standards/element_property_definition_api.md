# 统一构件属性定义新增接口

`POST /api/elements/{elementId}/properties/definitions` 要求
`Authorization: Bearer <登录会话令牌>`。服务器调用现有会话验证服务，并以服务器
返回的用户名判断是否属于 `admin`、`sa`、`SYSDBA` 管理员名称集；不信任
`X-Operator-Name`。同时必须提供一个 `X-Element-Definition-Key` 请求头；该密钥与
登录会话相互独立。操作日志不记录令牌、密钥或摘要，本接口的请求体和查询参数均不记录。
现有会话验证服务会更新会话的最后访问时间；其他旧接口的管理员判断不变。

**授权限制：**现有用户注册和修改接口未提供可靠的管理员权限保护，管理员用户名
和会话不能单独作为可信的管理员授权依据。独立写入密钥是本接口的关键保护；
只有可信操作人员可以持有密钥。不要将此方案视为已修复旧用户管理接口的安全问题。

## 连接要求

**目标部署：**仅支持客户端直连、由 IIS 站点直接终止 HTTPS 的进程内托管。
站点前存在任何负载均衡或反向代理的情况均不支持，须重新安全设计；
部署时须通过网络和站点配置确保这一前提。

**待核实，暂勿发布：**在本应用调用 `UseForwardedHeaders` 之前，从服务器特征
检查 IIS 进程内 `IIISEnvironmentFeature`，并从请求 `IServerVariablesFeature`
读取服务器变量 `HTTPS`。仅当两个特征均存在且变量为 `ON`（不区分大小写）时
捕获允许标记；特征或变量缺失、读取失败以及其他值均拒绝。不回退到
`Request.IsHttps`、转发头或 TLS 握手特征。该方案不支持其他托管方式。

缺少标记、包括环回在内的 `HTTPS=OFF` 以及常见代理转发头均被拒绝；
仅设置 `X-Forwarded-Proto` 或自定义转发协议头不能启用接口。旧接口不受此限制。
操作端必须在附加令牌或密钥之前确认使用直连 HTTPS；服务器拒绝 HTTP 请求无法撤回
已经在明文链路上传出的凭据。

若代理去除所有转发标记并重新建立 TLS，服务器无法仅凭协议区分代理和直接客户端；
部署时必须禁止此类转发。
Microsoft 文档说明 IIS 进程内由 IIS HTTP Server 接收并转交请求：
https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/in-process-hosting
微软文档说明 IIS 集成的默认前置转发处理中间件针对进程外托管；这不能排除其他
宿主配置或启动过滤器在本应用捕获前改写协议：
https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer

### 目标 IIS 验证要求

此方案**尚未在真实 IIS 验证，不得投入生产**：`IIISEnvironmentFeature` 提供 IIS
应用信息，不能单独证明请求由客户端直连或证明 TLS 链路；必须在目标 IIS 进程内
验证这些特征的实际可用性和来源。IIS URL Rewrite 等模块可以设置服务器变量，
应核实站点配置及
更早执行的模块没有改写 `HTTPS`，并验证真实 HTTPS 为 `ON`、HTTP 及伪造转发头
不能通过。使用隔离测试环境和测试凭据，勿记录实际密钥。

不支持代理或负载均衡终止 TLS；即使代理到 IIS 使用 HTTPS，`HTTPS=ON` 也只能说明
该连接，不证明客户端到代理的链路安全。前置代理及负载均衡场景须另行安全设计。

依据：
- https://learn.microsoft.com/previous-versions/iis/6.0-sdk/ms524602(v=vs.90)
- https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.server.iis.httpcontextextensions.getiisservervariable?view=aspnetcore-8.0
- https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.server.iis.iiisenvironmentfeature?view=aspnetcore-8.0
- https://learn.microsoft.com/iis/extensions/url-rewrite-module/setting-http-request-headers-and-iis-server-variables

## 密钥生成和部署

在可信环境生成 32 个密码学安全随机字节并编码为无填充的 43 字符 Base64URL
文本；按该文本的 UTF-8 字节计算 SHA-256，转为 64 位十六进制摘要。例如以下
PowerShell 仅在内存生成变量，不输出或保存值：

```powershell
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$keyText = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($keyText))
    $hashText = [BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
} finally { $sha.Dispose() }
```

通过安全渠道分别交付：服务器仅在进程环境配置
`ElementPropertyDefinitions__WriteKeySha256` 为生成的摘要；操作端独立安全保存
`$keyText` 并放入单个 `X-Element-Definition-Key` 请求头。两端不得将明文密钥或摘要
写入代码、日志、命令历史或不可信存储；使用受保护的密钥管理系统和 HTTPS。
服务器配置缺失或格式无效时接口关闭并返回 503；密钥缺失、格式无效或不匹配返回
403。该配置不影响其他接口，也不需要修改应用配置文件。

## 数据库连接一致性

新增接口在打开数据库连接前，检查服务器**启动后生效的配置**：
`Database:ConnectionString` 必须非空；构件服务所选的类型专用连接
`ConnectionStrings:MySQL` 或 `ConnectionStrings:DM` 未设置时回退到该通用连接。
若类型专用连接已设置为空，或两个服务实际使用的连接字符串不同，拒绝新增并返回
不含连接详情的 500。仅去除首尾空白后按大小写敏感的序数比较，不重排参数、
不等价转换；即使两段连接字符串可能指向同一数据库，只要文本不一致也会拒绝。

服务器启动时可能将类型专用连接填入缺失的通用连接，因此排查时以启动后生效的
配置为准。不得在日志、接口响应或工单中输出连接字符串或其他凭据；此检查不改变
原有查询和属性值保存接口。

请求体是 1～256 项的 JSON 数组，例如：

```json
[
  { "code": "DN", "name": "公称通径", "dataType": "TEXT", "unit": null, "isRequired": true, "sortOrder": 10 },
  { "code": "PN", "name": "公称压力", "dataType": "TEXT", "unit": null, "isRequired": false, "sortOrder": 20 },
  { "code": "MEDIUM", "name": "介质", "dataType": "TEXT", "unit": null, "isRequired": false, "sortOrder": 30 }
]
```

`code`、`name`、`dataType` 非空，去除首尾空白后长度分别不超过 128、255、32；
`unit` 可空，长度不超过 64；`isRequired`、`sortOrder` 必填。类型支持
`TEXT`，类型不区分大小写并以大写保存；`NUMBER`、`BOOLEAN` 尚不支持完整保存，当前拒绝新增。编码和名称在
批次内及该构件已有定义中均忽略大小写检查；拒绝控制字符。只接受
`DRAFT`、`REVIEW`、`PUBLISHED` 构件。

成功返回 201，`data` 为创建的定义数组，含 `id`、`elementDefinitionId`、
`propertyCode` 等字段；可将 `id` 作为现有 PUT 属性值接口的
`propertyDefinitionId`。本接口只新增定义，不新增或覆盖属性值。现有 PUT
仍按原有行为保存 `valueText`，本接口不改变其数值/布尔值保存能力。
输入无效返回 400；会话无效返回 401；无管理员权限返回 403；构件不存在返回 404；构件状态、
定义重复或数据库唯一键冲突返回 409；数据库故障返回不含内部细节的 500。
会话验证服务不可用或写入密钥摘要未正确配置时返回 503，不执行写入。

事务锁定构件行，串行化本接口对同一构件的新增和构件状态更新，并在错误时
回滚；数据库约束仍是 ID 和编码冲突的最终保障。数据库脚本没有名称唯一约束，
外部绕过本接口直接写表的并发操作不受该接口的名称检查保护。
若提交时连接中断，客户端无法仅凭错误判断提交结果，应先查询定义再决定是否重试。

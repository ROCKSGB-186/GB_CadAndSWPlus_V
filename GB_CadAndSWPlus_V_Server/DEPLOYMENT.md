# GB_CadAndSWPlus_V_Server 部署说明

## 1. 发布

在服务器项目目录执行：

```powershell
dotnet publish .\GB_CadAndSWPlus_V_Server.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

也可以使用 Visual Studio 的 `Properties/PublishProfiles/IISProfile.pubxml` 文件夹发布配置。

## 2. 数据库配置

编辑服务器实际运行目录中的 `appsettings.json`，例如：

```text
D:\GB_NewCadPlus_IV.UploadApi\appsettings.json
```

确认以下配置。`ConnectionStrings:DM` 不能保持为空：

```json
{
  "Database": {
	"Type": "DM",
	"Schema": "CAD_SW_LIBRARY",
	"ConnectionString": ""
  },
	"ConnectionStrings": {
	"DM": "Server=127.0.0.1;Port=5236;User Id=SYSDBA;Password=服务器实际密码;"
  }
}
```

服务器启动时会统一兼容以下配置来源，优先级从高到低由 .NET 配置提供程序决定：IIS/系统环境变量、`Database:ConnectionString`、`ConnectionStrings:DM`。当当前数据库类型的 `ConnectionStrings` 配置有效而通用配置为空时，服务器会在内存中自动补齐 `Database:ConnectionString`，兼容旧服务代码。不要把生产密码提交到 Git。

也可以不修改 JSON，使用 Windows 服务或 IIS 应用程序的环境变量配置。仓库中的 `web.config` 只包含注释模板，必须在服务器实际运行目录的 `web.config` 中填写真实值：

```text
ConnectionStrings__DM=Server=127.0.0.1;Port=5236;User Id=SYSDBA;Password=服务器实际密码;
```

IIS 的 `<environmentVariables>` 中应增加同名的 `ConnectionStrings__DM` 环境变量。不要只修改仓库中的源文件后直接启动旧的服务器目录；发布完成后应将新的 `web.config`、`appsettings.json` 和程序文件一起复制到 IIS 实际站点目录。

配置环境变量后必须重启 API 进程，启动日志应出现：

```text
服务器数据库配置状态。DatabaseType=DM; Schema=CAD_SW_LIBRARY; DMConnectionConfigured=True
```

确认 DM 服务在服务器本机监听 `5236`，并且 `CAD_SW_LIBRARY` Schema 以及以下表已存在或已完成初始化：

- `SYSTEM_CONFIG`
- `LAYER_DICTIONARY`
- `DEPARTMENTS`
- `USERS`
- `CAD_CATEGORIES`

## 3. 启动端口

生产配置默认监听：

```text
http://0.0.0.0:10010
```

发布后可在 `appsettings.json` 中通过 `Server:Port` 配置端口；`Server:Urls` 非空时优先使用完整地址，也可以通过 `ASPNETCORE_URLS` 覆盖监听地址。`launchSettings.json` 只用于开发启动。

服务器防火墙和云安全组需要允许配置的 API TCP 端口。DM 的 `5236` 通常只需要允许服务器本机访问，不建议对公网开放。

## 4. 部署后检查

在服务器本机先检查 DM 端口：

```powershell
Test-NetConnection 127.0.0.1 -Port 5236
```

确认 `TcpTestSucceeded` 为 `True` 后，重启实际运行的 API 服务、IIS 应用程序池或 `GB_CadAndSWPlus_V_Server.exe` 进程，再检查 API 进程和 DM 连接：

```powershell
Invoke-WebRequest http://服务器地址:10010/api/health
```

成功响应：

```json
{"success":true,"service":"ok","database":"ok"}
```

如果 API 正常但 DM 不可用，会返回 HTTP 503：

```json
{"success":false,"service":"ok","database":"unavailable"}
```

健康检查返回 `database:ok` 后，再检查部门接口：

```powershell
Invoke-WebRequest http://服务器地址:10010/api/departments
```

只有健康检查和部门接口均成功后，客户端才应测试登录。客户端的 `DatabasePort=5236` 不会替代服务器端 DM 连接配置。

再测试系统配置：

```powershell
Invoke-WebRequest http://服务器地址:10010/api/system-config/Version
```

写入配置时使用：

```powershell
Invoke-WebRequest -Uri http://服务器地址:10010/api/system-config/ClientVersion -Method Put -ContentType 'application/json' -Body '{"value":"3.26.0907.159"}'
```

## 5. 客户端配置

客户端 `App.config` 中配置：

```xml
<add key="ApiServerHost" value="服务器地址" />
<add key="ApiServerScheme" value="https" />
<add key="ApiConnectTimeoutSeconds" value="10" />
<add key="ApiRequestTimeoutSeconds" value="60" />
```

客户端所有数据库相关操作（包括图元下载、预览图、上传、用户、部门、分类、图层字典、系统配置和规范管理）均通过 LoginWindow 中配置的 API 主机、协议和端口访问 API。公网云部署应使用 HTTPS 和有效证书；客户端不关闭证书校验。数据库物理端口仍只由服务器端数据库连接配置使用。

## 6. 云部署检查清单

- 云安全组、防火墙、Nginx/IIS/负载均衡放行同一个 API 端口。
- 反向代理转发 `X-Forwarded-For` 和 `X-Forwarded-Proto`，并启用 `Server:ForwardedHeadersEnabled`。
- 代理上传限制应不小于服务端接口限制；图元上传接口当前最大约 1GB，规范文件接口最大约 512MB。
- 代理读取超时应大于客户端 `ApiRequestTimeoutSeconds`，大文件上传下载建议使用至少 10 分钟的代理超时。
- 仅对外暴露 API 端口；数据库端口不要直接暴露公网。
- 云端公网地址应使用域名和可信 TLS 证书，不要在客户端关闭证书验证。
- 延迟较高时优先提高客户端超时，不要对上传、更新、删除和登录盲目自动重试，避免重复写入。

## 7. 发布后配置保护

- 发布包中的 `appsettings.json` 和 `web.config` 只提供无密码模板；生产密码仅写入服务器实际配置或受保护的系统环境变量。
- 不要把服务器实际 `web.config`、包含密码的 `appsettings.json` 或 stdout 日志提交到 Git。
- 修改 IIS 配置后回收对应应用程序池；修改系统环境变量后必须重启 IIS 应用程序或 API 进程。
- 启动日志中的 `DMConnectionConfigured=True` 只表示连接串已被读取；仍需通过 `/api/health` 验证 DM 可连接，再通过 `/api/departments` 验证业务 Schema 和表结构。

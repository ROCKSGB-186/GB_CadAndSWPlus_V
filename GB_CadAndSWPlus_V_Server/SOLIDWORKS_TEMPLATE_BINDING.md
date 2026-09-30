# SolidWorks 模板真实附件绑定说明

## 一、上传模板文件

SolidWorks 模板通过现有规范附件接口上传，服务器不会让客户端直接访问数据库。

接口：

```text
POST /api/standards/management/versions/{versionId}/files
```

表单参数：

- `file`：`.sldasm` 或 `.sldprt` 文件
- `fileRole`：建议填写 `SW_TEMPLATE`
- `description`：填写平台代码，例如 `GB-PIPELINE SolidWorks 2022 平台装配体模板`
- `X-Operator-Name`：管理员用户名

上传成功后，服务器返回：

```json
{
  "success": true,
  "fileId": 123,
  "fileName": "GB-PIPELINE.sldasm"
}
```

记录返回的 `fileId`，后续需要写入服务器配置。

## 二、配置平台模板 FileId

在服务器 `appsettings.json` 或部署环境变量中配置：

```json
{
  "SolidWorks": {
	"PlatformTemplates": {
	  "GB-PIPELINE": {
		"FileId": 123
	  },
	  "GB-FLANGE": {
		"FileId": 124
	  },
	  "GB-BLIND-FLANGE": {
		"FileId": 125
	  }
	}
  }
}
```

实际部署时，将 `123`、`124`、`125` 替换成真实附件 ID。

也可以使用环境变量：

```text
SolidWorks__PlatformTemplates__GB-PIPELINE__FileId=123
```

## 三、验证服务器模板接口

请求：

```text
GET /api/sw-platform-templates/GB-PIPELINE
```

成功时应返回：

```json
{
  "success": true,
  "platformCode": "GB-PIPELINE",
  "fileId": 123,
  "fileName": "GB-PIPELINE.sldasm",
  "version": "文件 SHA256",
  "downloadPath": "api/standards/management/files/123/download"
}
```

如果模板没有配置、附件不存在、文件为空或扩展名不是 `.sldasm`/`.sldprt`，接口会返回 `success=false`，SolidWorks 客户端继续使用本地缓存或 Resources。

## 四、管理员检查绑定

管理员可以先调用绑定检查接口，不必直接打开 SolidWorks：

```text
GET /api/sw-platform-templates/GB-PIPELINE/binding-check
X-Operator-Name: admin
```

检查结果中的 `isUsable` 必须为 `true`，并且同时满足：

- `configuredFileId` 大于 0；
- `fileExists` 为 `true`；
- `isSolidWorksFile` 为 `true`；
- `hasContent` 为 `true`；
- 文件扩展名为 `.sldasm` 或 `.sldprt`。

## 五、SolidWorks 客户端执行顺序

1. 登录服务器。
2. 打开“管道”页面。
3. 点击“创建/打开平台装配体”。
4. 客户端查询 `GB-PIPELINE` 模板。
5. 服务器模板可用时下载并写入本地缓存。
6. 网络不可用时使用本地缓存。
7. 本地缓存不存在时使用 Resources 内置模板。
8. 所有模板均不存在时创建空装配体。

## 六、注意事项

- 模板附件实际文件必须是 SolidWorks 2022 可打开的 `.sldasm` 或 `.sldprt`。
- 服务器只返回 API 下载路径，不返回文件系统物理路径。
- 模板文件和业务图元仍由服务器统一管理，SolidWorks AddIn 不直接连接数据库。
- 修改 `appsettings.json` 后需要重启服务器，环境变量修改后也需要重启服务器进程。
- 未配置真实 FileId 时，示例中的 `0` 会触发客户端离线回退，不会阻断本地功能。

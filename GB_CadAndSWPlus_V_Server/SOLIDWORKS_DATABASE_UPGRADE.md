# SolidWorks 一期数据库升级说明

本文用于在已有 CAD 数据库基础上增加 SolidWorks 一期能力。升级过程不会删除现有 CAD 图元、用户、部门、分类和规范数据。

## 一、数据库对象关系

SolidWorks 不单独复制一套 CAD 图元库，而是复用现有服务器数据：

| 业务 | 复用或新增对象 | 说明 |
|---|---|---|
| 人员登录 | `USERS` | 复用现有用户表 |
| 部门 | `DEPARTMENTS` | 复用现有部门表 |
| CAD 分类 | `CAD_CATEGORIES`、`CAD_SUBCATEGORIES` | 复用现有分类表 |
| CAD 图元文件 | `CAD_FILE_STORAGE` | SolidWorks 通过服务器 API 查询和下载 |
| CAD 图元属性 | `CAD_BLOCK_ATTRIBUTES_JSON` | 复用统一属性 JSON |
| SolidWorks 模板 | `STANDARD_DOCUMENT_FILES` | 通过平台配置中的 FileId 绑定 `.sldasm` 或 `.sldprt` |
| SolidWorks 实例 | `SW_MODEL_INSTANCES` | 本次一期新增，用于记录 CAD PID 与 SW 组件的关联 |

SolidWorks 客户端不直接连接数据库，也不直接读取服务器物理文件路径。

## 二、执行前准备

1. 备份当前 DM 或 MySQL 数据库。
2. 确认服务器配置中的数据库类型与实际数据库一致：

```json
{
  "Database": {
	"Type": "DM",
	"Schema": "CAD_SW_LIBRARY"
  }
}
```

MySQL 时将 `Type` 设置为 `MYSQL`；DM 时通常使用 `CAD_SW_LIBRARY` Schema。
3. 确认数据库账号有创建表、修改表和创建索引权限。
4. 不要执行 `reset_*` 脚本作为升级方式；这些脚本可能删除测试或业务表。

## 三、执行升级脚本

### 达梦 DM

使用达梦 SQL 执行工具，以能够访问 `CAD_SW_LIBRARY` 的账号执行：

```text
Resources/Standards/upgrade_solidworks_schema_dm.sql
```

脚本会：

- 创建 `CAD_SW_LIBRARY.SW_MODEL_INSTANCES`（不存在时）
- 为旧版本实例表补齐缺失字段
- 创建 PID 和图元库索引
- 输出升级后的字段和索引检查结果

DM 脚本使用 `ALL_TABLES`、`ALL_TAB_COLUMNS` 和 `ALL_INDEXES` 判断对象是否存在，可以重复执行。

### MySQL

连接到服务器配置中 `ConnectionStrings:MySQL` 对应的数据库，执行：

```text
Resources/Standards/upgrade_solidworks_schema_mysql.sql
```

脚本会：

- 创建 `sw_model_instances`（不存在时）
- 使用 `information_schema` 检查并补齐字段
- 创建缺失的查询索引
- 输出升级后的字段和索引检查结果

## 四、升级后验证

### DM

```sql
SELECT COUNT(*)
FROM ALL_TABLES
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND TABLE_NAME = 'SW_MODEL_INSTANCES';

SELECT COLUMN_NAME, DATA_TYPE
FROM ALL_TAB_COLUMNS
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND TABLE_NAME = 'SW_MODEL_INSTANCES'
ORDER BY COLUMN_ID;

SELECT INDEX_NAME
FROM ALL_INDEXES
WHERE OWNER = 'CAD_SW_LIBRARY'
  AND TABLE_NAME = 'SW_MODEL_INSTANCES';
```

### MySQL

```sql
SELECT COUNT(*)
FROM information_schema.tables
WHERE table_schema = DATABASE()
  AND table_name = 'sw_model_instances';

SELECT column_name, data_type
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'sw_model_instances'
ORDER BY ordinal_position;

SHOW INDEX FROM sw_model_instances;
```

## 五、平台模板配置

平台模板不需要新建 SolidWorks 专用文件表。先通过服务器规范附件管理接口上传 `.sldasm` 或 `.sldprt`，得到 `STANDARD_DOCUMENT_FILES.ID`，再在服务器配置中绑定：

```json
{
  "SolidWorks": {
	"PlatformTemplates": {
	  "GB-PIPELINE": { "FileId": 123 },
	  "GB-FLANGE": { "FileId": 124 },
	  "GB-BLIND-FLANGE": { "FileId": 125 }
	}
  }
}
```

绑定后检查：

```text
GET /api/sw-platform-templates/GB-PIPELINE/binding-check
```

模板必须满足：

- 文件存在
- 扩展名为 `.sldasm` 或 `.sldprt`
- 文件大小大于 0
- 服务器存储路径可读取

## 六、一期 SolidWorks 实例写入流程

SolidWorks 插入图元后调用：

```text
POST /api/sw-instances/register
```

服务器写入 `SW_MODEL_INSTANCES`：

- `SW_INSTANCE_ID`：SolidWorks 组件实例标识
- `PID_INSTANCE_ID`：CAD PID 图元实例标识
- `LIBRARY_ITEM_ID`：统一图元库文件 ID
- `DOCUMENT_PATH`：SolidWorks 装配体路径
- `COMPONENT_NAME`：插入后的组件名称
- `TRANSFORM_JSON`：组件位置和变换信息
- `CUSTOM_PROPERTIES_JSON`：统一属性
- `VERSION`：乐观并发版本
- `SYNC_STATUS`：同步状态

## 七、当前一期不应执行的操作

- 不要删除或重建 `USERS`、`DEPARTMENTS`、`CAD_CATEGORIES`、`CAD_FILE_STORAGE`。
- 不要在 SolidWorks 客户端增加数据库连接字符串。
- 不要把服务器返回的 `FilePath` 直接交给客户端使用。
- 不要把 CAD 图元复制到新的 SolidWorks 图元表；统一使用 `CAD_FILE_STORAGE`。
- 不要用 `reset_cad_sw_library_dm.sql` 代替增量升级脚本。

## 八、升级完成后的服务器配置检查

确认服务器 `appsettings.json` 中：

- `Database:Type` 与实际数据库一致
- `Database:Schema` 与 DM Schema 一致
- `ConnectionStrings:DM` 或 `ConnectionStrings:MySQL` 可用
- `Storage:Root` 指向图元文件实际存储根目录
- `SolidWorks:PlatformTemplates:*:FileId` 指向有效附件 ID
- `Server:Port` 与客户端登录页面 API 端口一致，默认是 `10010`

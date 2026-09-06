# 多平台核心实体与版本规则设计

## 变更边界

本设计仅定义后续新增表和 API 的数据边界。不得修改、删除或重命名现有 `STANDARD_FAMILIES`、`STANDARD_DOCUMENTS`、`STANDARD_SERIES`、`STANDARD_FLANGE_RECORDS`、图元文件表及其历史数据。

实际迁移仅允许执行新增表、外键、索引和只读视图。执行前必须备份并在测试库验证；生产库执行另行确认。

## 新增核心实体

| 实体 | 主键 | 作用 | 与既有数据的关系 |
| --- | --- | --- | --- |
| `CATALOG_ITEMS` | `ID` + `CATALOG_ITEM_ID`(GUID) | 统一图元库条目，承载蝶阀、法兰、盲板及后续设备。 | 使用 `FAMILY_CODE`、`ITEM_CODE` 与 `STANDARD_*` 记录建立映射，不复制或替代规范原始记录。 |
| `CATALOG_ITEM_REVISIONS` | `ID` | 图元库发布版本。 | 一个图元可有多个修订，只有一个当前已发布修订。 |
| `CATALOG_PLATFORM_RESOURCES` | `ID` | 平台资源映射：DWG、SLDPRT、SLDASM、RFA、DGN 等。 | CAD 现有图元文件继续使用；通过引用/映射关联，不迁移文件。 |
| `CATALOG_PORTS` | `ID` + `PORT_CODE` | 库图元连接口定义。 | 属于指定图元修订；不写入既有法兰标准表。 |
| `PROJECTS` | `ID` + `PROJECT_ID`(GUID) | 工程项目容器。 | 后续项目级对象的根。 |
| `PROJECT_OBJECTS` | `ID` + `PROJECT_OBJECT_ID`(GUID) | 工程中实际插入的设备、阀门、法兰、盲板、管道实例。 | 首次发布时可记录历史 `PIPEID`，但不修改其格式。 |
| `PROJECT_OBJECT_PROPERTIES` | `ID` | 统一属性键值及来源。 | 与字段字典关联；兼容读取 CAD 属性/XRecord。 |
| `PID_CONNECTIONS` | `ID` + `CONNECTION_ID`(GUID) | P&ID 的对象—端口逻辑连通关系。 | 初始由 CAD 端点识别和拓扑功能导出。 |
| `PLATFORM_BINDINGS` | `ID` | 工程对象在 AutoCAD、SolidWorks 等平台中的定位信息。 | 保存 CAD 句柄、DWG 标识、SolidWorks 装配组件持久化引用等。 |
| `SYNC_CHANGESETS` | `ID` + `CHANGESET_ID`(GUID) | 单次推送/拉取的同步批次。 | 支持幂等、审计和失败重试。 |
| `SYNC_CONFLICTS` | `ID` | 同一字段的并发或所有权冲突。 | 不自动覆盖，必须人工解决。 |
| `ENGINEERING_AUDIT_LOGS` | `ID` | 独立于现有操作日志的工程审计记录。 | 记录对象、版本、操作者、来源平台和变更摘要。 |

## 关键关系

```text
STANDARD_FAMILIES / STANDARD_SERIES / STANDARD_FLANGE_RECORDS
						 |
						 +--> CATALOG_ITEMS --> CATALOG_ITEM_REVISIONS
												   |             |
												   |             +--> CATALOG_PORTS
												   +--> CATALOG_PLATFORM_RESOURCES

PROJECTS --> PROJECT_OBJECTS --> PROJECT_OBJECT_PROPERTIES
				  |       |              |
				  |       |              +--> SYNC_CHANGESETS / SYNC_CONFLICTS
				  |       +--> PLATFORM_BINDINGS
				  +--> PID_CONNECTIONS <--> CATALOG_PORTS
```

## 标识与并发规则

1. `CATALOG_ITEM_ID`、`PROJECT_ID`、`PROJECT_OBJECT_ID`、`CONNECTION_ID`、`CHANGESET_ID` 均使用服务端生成的 GUID，生成后不可变。
2. 关系型数值 `ID` 仅用于数据库关联和性能，不暴露为跨平台业务主键。
3. 每个可修改实体包含 `REVISION_NO`、`CREATED_AT`、`UPDATED_AT`、`CREATED_BY`、`UPDATED_BY`、`IS_DELETED`。
4. 更新 `PROJECT_OBJECTS` 或其属性时必须携带预期 `REVISION_NO`。不一致时创建 `SYNC_CONFLICTS`，返回冲突，不覆盖。
5. 已发布的 `CATALOG_ITEM_REVISIONS` 不允许原地修改；需要更改时创建新修订并重新发布。
6. `CATALOG_PLATFORM_RESOURCES` 记录文件哈希、相对路径、平台、资源类型和适用版本；本机绝对路径不得作为业务数据。

## 字段值存储策略

1. 稳定且高频筛选的字段（图元编码、分类、DN、PN、材质、状态、版本）保存为结构化列或可查询索引。
2. 专业规范扩展字段保留在 JSON 中，但需由统一字段代码约束，禁止任意中文键名直接进入跨平台 API。
3. `PROJECT_OBJECT_PROPERTIES` 至少包含：对象 GUID、字段代码、文本值、数值值、单位、来源平台、写入时间、属性版本。
4. CAD 当前的块属性与 `GBPIPE_DATA` 为兼容缓存；服务端工程对象属性为跨平台同步依据。

## 版本与发布状态

| 对象 | 建议状态 | 规则 |
| --- | --- | --- |
| 图元库修订 | `Draft`、`Review`、`Released`、`Superseded`、`Archived` | 只有 `Released` 可被项目新插入引用。 |
| 工程对象 | `Draft`、`Released`、`Changed`、`Obsolete` | P&ID 变更后标为 `Changed`，等待 SolidWorks 差异处理。 |
| 平台绑定 | `InSync`、`PendingPush`、`PendingPull`、`Conflict`、`Error` | 绑定状态独立于工程对象状态。 |
| 同步变更集 | `Created`、`Validating`、`Applied`、`PartiallyApplied`、`Rejected`、`Failed` | 每一状态转换写审计记录。 |

## 首批试点落库范围

1. 建立“蝶阀（法兰）”“法兰”“管端盲板”三条 `CATALOG_ITEMS` 记录，并分别关联现有 `ITEM_CODE`、分类和规范系列。
2. 每个试点图元建立一个初始 `Released` 修订。
3. 每个蝶阀和法兰建立 `P1`、`P2` 两个双向连接口；管端盲板建立 `P1` 单端连接口。
4. 三维资源在首期可为空，但资源表、版本、哈希和 SolidWorks 2022 适用版本字段必须准备就绪。
5. 不回填历史 CAD 图纸；仅在用户主动发布或同步图纸时为对象创建 `PROJECT_OBJECTS` 和平台绑定。

## 数据迁移原则

- 先建新表，再创建只读兼容视图，再启用新 API，最后由用户主动触发单张图纸迁移。
- 任一步失败只标记同步失败和保留原 CAD 图纸，不删除、不回滚用户图形对象。
- 旧接口继续读取现有图元/规范表；新接口在没有新记录时回退到旧数据。
- 达梦和 MySQL 分别维护等价的新增迁移脚本；不得将某个数据库专有语法泄漏到业务 API。
# 统一工程数据字典（第一期）

## 适用范围

本字典同时覆盖工艺管道、蝶阀（法兰）、法兰和管端盲板。首个适配目标为 SolidWorks 2022；字段与接口不得使用仅某个 SolidWorks 版本存在的业务语义，以支持后续版本适配。

本文件只定义统一数据语义和映射，不修改现有数据库、API、DWG 属性或 SolidWorks 文件。

## 规则

1. 字段代码使用英文 PascalCase，作为服务端 API、数据库和平台映射的稳定键。
2. CAD 现有 Tag 继续保留；新代码优先使用统一字段代码，但兼容读取既有 Tag。
3. 业务对象以 `ProjectObjectId`（GUID）唯一关联；`PIPEID`、CAD ObjectId、句柄、块名和 SolidWorks 特征名称均只能作为平台内定位信息。
4. 字段所有权决定最终值来源。非所有者平台编辑字段时，仅产生差异记录，不覆盖主数据。
5. 长度默认使用 `mm`，质量使用 `kg`，压力等级使用文本标准值（例如 `PN16`）；日期使用 ISO 8601 UTC。

## 通用身份与生命周期字段

| 字段代码 | 类型 | 必填 | 所有者 | 当前 CAD 对应 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `ProjectObjectId` | GUID | 是（新增对象） | 工程数据服务 | 新增，兼容期保留 `PIPEID` | 跨平台项目实例主键。 |
| `CatalogItemCode` | Text | 是 | 图元库 | `ITEM_CODE` | 图元/物料编码。 |
| `CatalogFamilyCode` | Text | 是 | 图元库 | `FAMILY_CODE` | 图元分类编码。 |
| `DisplayName` | Text | 是 | 图元库 | `ITEM_NAME` | 受控显示名称。 |
| `CatalogRevision` | Text | 是 | 图元库 | 新增 | 图元、模板和端口定义的发布版本。 |
| `ProjectRevision` | Integer | 是 | 工程数据服务 | 新增 | 乐观并发版本，更新时必须回传。 |
| `LifecycleStatus` | Enum | 是 | 工程数据服务 | 新增 | `Draft`、`Released`、`Superseded`、`Archived`。 |
| `SourcePlatform` | Enum | 是 | 工程数据服务 | 新增 | `AutoCAD`、`SolidWorks`、`Revit`、`Bentley`、`Service`。 |

## 通用工程与规范字段

| 字段代码 | 类型/单位 | 必填 | 所有者 | 当前 CAD 对应 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `NominalDiameter` | Text | 是 | 工艺/规范 | `DN` | 公称直径，例如 `DN100`。 |
| `NominalDiameterValue` | Integer | 否 | 规范 | `DNValue` | 用于排序、匹配和计算。 |
| `PressureRating` | Text | 否 | 工艺/规范 | `PN` | 压力等级，例如 `PN16`。 |
| `Material` | Text | 否 | 工艺/规范 | `MATERIAL` | 材质或材质牌号。 |
| `ConnectionType` | Enum | 是 | 图元库/规范 | `CONNECTION_TYPE` | 法兰、焊接、螺纹、卡箍等。 |
| `StandardNumber` | Text | 否 | 规范 | `DrawingStandardNo` | 采用的 GB 或其他规范号。 |
| `Schedule` | Text | 否 | 工艺/规范 | `Schedule` | 壁厚等级。 |
| `Medium` | Text | 否 | 工艺 | `Medium` | 介质或介质编码。 |
| `Description` | Text | 否 | 工程数据服务 | `Description` | 项目级补充说明。 |

## 管道字段

| 字段代码 | 类型/单位 | 必填 | 所有者 | 当前 CAD 对应 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `PipelineNumber` | Text | 推荐 | 工艺 | 待从现有属性确认 | 工艺管线号。 |
| `PipelineRole` | Enum | 是 | CAD P&ID | `PIPE_ROLE` | 当前值为 `IMPORT` 或 `EXPORT`。 |
| `PipelineLength` | Decimal/mm | 是 | CAD 几何或 SolidWorks 三维 | `PIPE_LENGTH` | 由当前发布平台重新计算，不作为手工输入字段。 |
| `OuterDiameter` | Decimal/mm | 推荐 | 规范 | `OuterDiameter` | 管外径。 |
| `WallThickness` | Decimal/mm | 推荐 | 规范 | `WallThickness` | 管壁厚。 |
| `FlowDirection` | Enum | 否 | 工艺 | 流向符号/属性 | 以 P&ID 逻辑方向为准。 |
| `StartPortId` | Text | 是 | 工程数据服务 | 新增 | 起点连接口标识。 |
| `EndPortId` | Text | 是 | 工程数据服务 | 新增 | 终点连接口标识。 |

## 阀门、法兰和盲板字段

| 字段代码 | 类型/单位 | 适用对象 | 所有者 | 当前来源 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `TagNumber` | Text | 阀门 | 工艺 | 待从现有图元属性确认 | 阀门位号。 |
| `ValveType` | Enum | 蝶阀 | 图元库 | 图元分类/扩展属性 | 例如 `ButterflyValve`。 |
| `ValveOperation` | Enum | 蝶阀 | 图元库/工程 | 手柄、蜗轮、电动、气动等。 |
| `FaceType` | Text | 法兰、蝶阀 | 规范 | 现有法兰规范 | 密封面类型。 |
| `FlangeOuterDiameter` | Decimal/mm | 法兰、蝶阀 | 规范 | `FlangeStandardExtensionDto` | 法兰外径。 |
| `BoltCircleDiameter` | Decimal/mm | 法兰、蝶阀 | 规范 | `FlangeStandardExtensionDto` | 螺栓中心圆直径。 |
| `BoltHoleDiameter` | Decimal/mm | 法兰、蝶阀 | 规范 | `FlangeStandardExtensionDto` | 螺栓孔径。 |
| `BoltCount` | Integer | 法兰、蝶阀 | 规范 | `FlangeStandardExtensionDto` | 螺栓孔数量。 |
| `FlangeThickness` | Decimal/mm | 法兰、蝶阀 | 规范 | `FlangeStandardExtensionDto` | 法兰厚度。 |
| `RaisedFaceHeight` | Decimal/mm | 法兰、蝶阀 | 规范 | `FlangeStandardExtensionDto` | 突面高度。 |
| `BlindEndType` | Enum | 管端盲板 | 图元库/规范 | 新增 | 法兰盲板、封头、堵头等。 |

## 平台关联与空间字段

| 字段代码 | 类型 | 所有者 | 说明 |
| --- | --- | --- | --- |
| `PlatformObjectBinding` | JSON | 平台适配器 | 平台、文档、对象持久化标识、创建时间和同步版本。 |
| `CadObjectHandle` | Text | AutoCAD | 仅用于在当前 DWG 中定位，不用于跨图纸关联。 |
| `SolidWorksDocumentPath` | Text | SolidWorks | 受控文件存储中的相对路径，不使用本机绝对路径作为业务键。 |
| `SolidWorksComponentReference` | Text | SolidWorks | 装配组件持久化引用。 |
| `PositionX/Y/Z` | Decimal/mm | SolidWorks | 三维实例位置。 |
| `Orientation` | JSON | SolidWorks | 三维实例姿态/变换矩阵。 |
| `SyncState` | Enum | 工程数据服务 | `InSync`、`PendingPush`、`PendingPull`、`Conflict`、`Error`。 |

## 连接口字段

每个库图元的连接口是独立记录，不写成不受约束的自由文本属性。

| 字段代码 | 类型 | 说明 |
| --- | --- | --- |
| `PortId` | Text | 同一图元内稳定且唯一的端口代码，例如 `P1`、`P2`。 |
| `PortName` | Text | 用户可读名称。 |
| `PortRole` | Enum | `Inlet`、`Outlet`、`Bidirectional`、`Drain`、`Vent`。 |
| `ConnectionType` | Enum | 与可连接管线/部件匹配。 |
| `NominalDiameter` | Text | 端口公称直径。 |
| `PressureRating` | Text | 端口压力等级。 |
| `LocalPosition` | JSON/mm | 相对三维模板原点的位置。 |
| `LocalDirection` | JSON | 端口朝向单位向量。 |
| `AllowedConnectionFamilies` | JSON | 允许连接的图元分类编码。 |

## 现有字段兼容映射

| 现有键 | 一期统一字段 | 处理方式 |
| --- | --- | --- |
| `FAMILY_CODE` | `CatalogFamilyCode` | 双向读取；历史 CAD 写入保持原键。 |
| `ITEM_CODE` | `CatalogItemCode` | 双向读取；作为库映射候选键，不替代 GUID。 |
| `ITEM_NAME` | `DisplayName` | 继续展示，受图元库控制。 |
| `DN`、`PN`、`MATERIAL`、`CONNECTION_TYPE` | 同名语义字段 | 继续保留现有 Tag，服务端映射到新字段代码。 |
| `PIPEID` | `ProjectObjectId` | 只在首次迁移时建立映射；不复用、不修改历史 `PIPEID`。 |
| `GBPIPE_DATA` | 统一字段的 CAD 缓存 | 原格式继续可读写；新增字段按兼容键策略追加。 |

## 禁止事项

- 不以块名、DWG 文件名、SolidWorks 文件名、位号或 CAD ObjectId 作为跨平台主键。
- 不让任一平台的显示属性替代数据库主数据。
- 不在未建立端口定义前实现自动连接或自动路由。
- 不在未确认字段所有权时执行自动双向覆盖。
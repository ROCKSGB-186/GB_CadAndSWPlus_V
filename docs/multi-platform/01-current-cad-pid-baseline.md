# 当前 CAD/P&ID 能力迁移基线

## 目的

本文件记录扩展 SolidWorks 前已经存在的 CAD、服务端与数据库能力。后续改造必须保持本文件列出的已有行为可用；涉及数据库破坏性变更、CAD 图纸存储格式变更或既有接口不兼容时，先设计兼容层并单独评审。

## 当前项目边界

| 层级 | 项目/位置 | 当前职责 | 迁移约束 |
| --- | --- | --- | --- |
| CAD 客户端 | `GB_CadAndSWPlus_V` | AutoCAD 图元插入、属性维护、管道落图、表格及标准查询 | 保持 `net48`、x64 和 AutoCAD API 引用，不引入 SolidWorks API。 |
| 服务端 | `GB_CadAndSWPlus_V_Server` | 图元文件、分类、规范、管道字段、权限和 API | 保持现有接口和数据库兼容；新能力以新增接口/字段提供。 |
| 图元资源 | `GB_CadAndSWPlus_V/Resources` | CAD DWG 图元及预览资源 | 现有 DWG 文件和分类路径继续可用；三维资源另行登记。 |
| 标准库 | `CAD_SW_LIBRARY` | 标准族、标准系列和法兰规范数据 | 现有标准表不改语义，新增多平台图元与连接口表。 |

## 已确认的 CAD 图元与属性机制

1. 图元文件通过分类查询接口加载，服务端 `GraphicDto` 已保存分类、文件名、哈希、版本、块名、图层、颜色、比例、预览和文件路径。
2. 通用规范图元属性目前使用 `FAMILY_CODE`、`ITEM_CODE`、`ITEM_NAME`、`DN`、`PN`、`MATERIAL`、`CONNECTION_TYPE`，专业扩展字段通过 `ExtraAttributes` 传递。
3. CAD 端规范属性同时保存在图元 JSON 字典、块属性和扩展字典中；这是兼容历史图纸的必要行为，迁移期间不得移除。
4. 管道使用 `GBPIPE_DATA` 扩展字典保存业务属性；业务属性键可经 `PipelineCadPropertyKeyHelper` 编码后写入 CAD 字典。
5. 新建管道会生成 `PIPEID`、`PIPE_ROLE`、`PIPELINETITLE`、`PIPE_LENGTH` 和属性载体块句柄；管道主体、隐藏属性块、标题与流向符号具有明确关联。
6. 现有端点识别可读取块属性和扩展字典属性，现有管道拓扑能力可按端点和业务属性判断并线。

## 当前 P&ID 可复用能力

| 能力 | 现有位置 | 后续用途 |
| --- | --- | --- |
| 管线对象持久化 | `Helpers/PipelineCadObjectService.cs` | 导出 P&ID 管线实例、属性与路径。 |
| 端点对象/属性识别 | `Helpers/PipelineEndpointPropertyHelper.cs` | 识别 P&ID 中管线与设备/阀门的逻辑连接。 |
| 管线连通性处理 | `Helpers/PipelineTopologyHelper.cs` | 形成并校验工艺网络。 |
| 管道字段目录 | 客户端/服务端 `Pipeline*Models.cs` | 升级为跨平台字段字典的初始来源。 |
| 规范属性回写 | `Helpers/StandardPropertySyncService.cs` | 保持 CAD 显示属性与数据库标准记录一致。 |

## 首批三维试点对象

第一批只建立以下对象的 CAD—数据库—SolidWorks 完整映射，用于验证端口、属性、版本与同步机制：

| 对象族 | 当前 CAD 基础 | 三维最小要求 |
| --- | --- | --- |
| 直管 | 进口/出口管道 DWG 与管线属性 | 长度、DN、PN、材质、两端连接口。 |
| 蝶阀 | `GB_PID_FMDF_蝶阀.dwg` | 阀门型号、DN、PN、材质、两端连接口。 |
| 法兰 | `GB_PID_FL_法兰.dwg` 和现有法兰标准 | 标准系列、DN、PN、密封面、螺栓参数、两端连接口。 |
| 异径管 | `GB_PID_YJJG_异径接管.dwg` | 两端 DN、材质、连接方式、两端连接口。 |
| 泵或储罐 | 需从现有库中选定真实 P&ID 图元 | 位号、型号、外形尺寸、设备连接口、三维朝向。 |

## 首批统一字段映射

| 统一字段代码 | 当前 CAD 来源 | 当前含义 | 一期字段归属 |
| --- | --- | --- | --- |
| `CatalogItemCode` | `ITEM_CODE` | 库图元/物料编码 | 图元库 |
| `CatalogFamilyCode` | `FAMILY_CODE` | 图元分类编码 | 图元库 |
| `DisplayName` | `ITEM_NAME` | 图元显示名称 | 图元库 |
| `NominalDiameter` | `DN` | 公称直径 | 工艺/规范 |
| `PressureRating` | `PN` | 公称压力 | 工艺/规范 |
| `Material` | `MATERIAL` | 材质 | 工艺/规范 |
| `ConnectionType` | `CONNECTION_TYPE` | 连接类型 | 图元库/规范 |
| `PipelineRole` | `PIPE_ROLE` | 进口或出口角色 | CAD P&ID |
| `PipelineLength` | `PIPE_LENGTH` | CAD 管线长度 | CAD 几何 |
| `ProjectObjectId` | 新增，兼容期不替换 `PIPEID` | 跨平台项目实例 GUID | 统一工程数据 |
| `CatalogRevision` | 新增 | 图元库资源版本 | 图元库 |
| `PlatformObjectBinding` | 新增 | 各平台对象关联标识 | 平台适配器 |

## 迁移兼容规则

1. `PIPEID`、现有 AttributeReference、XRecord 和 `GBPIPE_DATA` 在兼容期内继续读写。
2. 新增 `ProjectObjectId` 时仅追加到现有属性载体/扩展字典，不修改现有键名或删除历史字段。
3. 现有图元文件上传、分类查询、标准查询和 CAD 插入流程保持原路由和响应字段不变；多平台资源采用新接口或可选字段。
4. SolidWorks 模型文件不放入 CAD `Resources` 目录，不修改现有 DWG 发布方式。
5. 所有首次同步均采用“只读比对/人工确认”模式；未确认前不得用任一平台的值覆盖另一个平台的数据。

## 本步骤的后续输入

进入统一数据字典设计前，需由业务侧确认首批“泵或储罐”的 CAD 图元、实际业务字段以及支持的 SolidWorks 目标版本。确认前不修改现有数据库 schema、API 或 CAD 图纸数据。
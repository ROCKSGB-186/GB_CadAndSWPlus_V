# CAD/SolidWorks 统一构件图元库一期脚本执行顺序

## 1. 备份

1. 备份 CAD_SW_LIBRARY 数据库。
2. 确认当前环境是测试库还是生产库。
3. 确认 `CAD_FILE_STORAGE`、`CAD_BLOCK_ATTRIBUTES_JSON`、`SW_MODEL_INSTANCES` 当前行数。
4. 确认当前 Schema 和数据库类型。

## 2. 达梦 DM

按以下顺序执行：

1. `element_library_schema_dm.sql`
2. `element_metadata_schema_dm.sql`
3. `element_library_pilot_seed_dm.sql`（仅在需要初始化试点身份时执行）
4. `upgrade_element_instance_schema_dm.sql`

撤销新增构件库结构时使用：

- `rollback_element_library_dm.sql`

回滚脚本不会删除 `SW_MODEL_INSTANCES` 上新增的可空字段；这些字段需要在确认没有新 API 依赖后人工处理。

## 3. MySQL

按以下顺序执行：

1. `element_library_schema_mysql.sql`
2. `element_metadata_schema_mysql.sql`
3. `element_library_pilot_seed_mysql.sql`（仅在需要初始化试点身份时执行）
4. `upgrade_element_instance_schema_mysql.sql`

撤销新增构件库结构时使用：

- `rollback_element_library_mysql.sql`

## 4. 验收检查

- 新增表存在且主键、唯一约束、外键和索引有效。
- 旧 CAD 表行数不变。
- 旧 SolidWorks 实例表行数不变。
- 三个试点构件编码唯一。
- 未执行任何旧文件迁移时，CAD 离线 Resources 仍可用。
- 未执行资源绑定前，不应把任意旧 CAD 文件自动认定为试点构件资源。

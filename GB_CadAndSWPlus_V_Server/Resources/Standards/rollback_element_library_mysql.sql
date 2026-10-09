-- CAD/SolidWorks 统一构件图元库一期回滚脚本：MySQL
-- 高风险操作：仅允许在确认需要撤销本次新增结构时执行。
-- 本脚本只删除本次新增表，不删除 cad_file_storage、cad_block_attributes_json、sw_model_instances。

SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS sw_connection_points;
DROP TABLE IF EXISTS sw_element_definitions;
DROP TABLE IF EXISTS element_property_values;
DROP TABLE IF EXISTS element_property_definitions;
DROP TABLE IF EXISTS element_resources;
DROP TABLE IF EXISTS element_definitions;
SET FOREIGN_KEY_CHECKS = 1;

-- 注意：本脚本不回滚 sw_model_instances 上的新增可空字段。
-- 如确需回滚实例字段，必须先确认没有新 API 或数据依赖，再人工执行 ALTER TABLE DROP COLUMN。

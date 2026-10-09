-- CAD PID 与 SolidWorks 构件实例关联升级脚本：MySQL
-- 作用：为现有 sw_model_instances 增加统一构件定义和项目标识字段。
-- 兼容原则：不删除、不重命名 library_item_id；旧客户端仍可继续读写原字段。

SET @table_exists = (
	SELECT COUNT(*) FROM information_schema.tables
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances'
);

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'element_definition_id'
), 'ALTER TABLE sw_model_instances ADD COLUMN element_definition_id BIGINT NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'project_code'
), 'ALTER TABLE sw_model_instances ADD COLUMN project_code VARCHAR(128) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'resource_id'
), 'ALTER TABLE sw_model_instances ADD COLUMN resource_id BIGINT NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'selected_configuration'
), 'ALTER TABLE sw_model_instances ADD COLUMN selected_configuration VARCHAR(255) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @index_exists = (
	SELECT COUNT(*) FROM information_schema.statistics
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND index_name = 'ix_sw_model_instances_element'
);
SET @sql = IF(@table_exists = 1 AND @index_exists = 0,
	'CREATE INDEX ix_sw_model_instances_element ON sw_model_instances (element_definition_id, project_code)',
	'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @index_exists = (
	SELECT COUNT(*) FROM information_schema.statistics
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND index_name = 'ix_sw_model_instances_resource'
);
SET @sql = IF(@table_exists = 1 AND @index_exists = 0,
	'CREATE INDEX ix_sw_model_instances_resource ON sw_model_instances (resource_id)',
	'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SELECT column_name, column_type
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'sw_model_instances'
  AND column_name IN ('library_item_id', 'element_definition_id', 'project_code', 'resource_id', 'selected_configuration')
ORDER BY ordinal_position;

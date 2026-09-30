-- SolidWorks 一期数据库增量升级脚本：MySQL 版本
-- 适用：已经存在 CAD_SW_LIBRARY 旧数据库的环境。
-- 作用：新增 CAD PID 与 SolidWorks 实例关联表，不删除、不修改既有 CAD、用户、部门、分类和规范数据。
-- 执行前：请先备份数据库，并确认当前账号具备 CREATE TABLE、ALTER、CREATE INDEX 权限。
/*

-- ============================================================
-- 1. 创建 SolidWorks 实例关联表
-- ============================================================
CREATE TABLE IF NOT EXISTS sw_model_instances (
	sw_instance_id VARCHAR(128) NOT NULL,
	pid_instance_id VARCHAR(128) NOT NULL,
	library_item_id INT NULL,
	document_path VARCHAR(1000) NULL,
	document_name VARCHAR(255) NULL,
	assembly_name VARCHAR(255) NULL,
	component_name VARCHAR(255) NULL,
	configuration_name VARCHAR(255) NULL,
	transform_json TEXT NULL,
	custom_properties_json JSON NULL,
	version INT NOT NULL DEFAULT 1,
	sync_status VARCHAR(32) NOT NULL DEFAULT 'SYNCED',
	created_at DATETIME NULL,
	updated_at DATETIME NULL,
	PRIMARY KEY (sw_instance_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ============================================================
-- 2. 为旧版本已存在的实例表补齐必要列
-- ============================================================
SET @table_exists = (
	SELECT COUNT(*)
	FROM information_schema.tables
	WHERE table_schema = DATABASE()
	  AND table_name = 'sw_model_instances'
);

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'pid_instance_id'
), 'ALTER TABLE sw_model_instances ADD COLUMN pid_instance_id VARCHAR(128) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'library_item_id'
), 'ALTER TABLE sw_model_instances ADD COLUMN library_item_id INT NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'document_path'
), 'ALTER TABLE sw_model_instances ADD COLUMN document_path VARCHAR(1000) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'document_name'
), 'ALTER TABLE sw_model_instances ADD COLUMN document_name VARCHAR(255) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'assembly_name'
), 'ALTER TABLE sw_model_instances ADD COLUMN assembly_name VARCHAR(255) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'component_name'
), 'ALTER TABLE sw_model_instances ADD COLUMN component_name VARCHAR(255) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'configuration_name'
), 'ALTER TABLE sw_model_instances ADD COLUMN configuration_name VARCHAR(255) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'transform_json'
), 'ALTER TABLE sw_model_instances ADD COLUMN transform_json TEXT NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'custom_properties_json'
), 'ALTER TABLE sw_model_instances ADD COLUMN custom_properties_json JSON NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'version'
), 'ALTER TABLE sw_model_instances ADD COLUMN version INT NOT NULL DEFAULT 1', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'sync_status'
), 'ALTER TABLE sw_model_instances ADD COLUMN sync_status VARCHAR(32) NOT NULL DEFAULT ''SYNCED''', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'created_at'
), 'ALTER TABLE sw_model_instances ADD COLUMN created_at DATETIME NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql = IF(@table_exists = 1 AND NOT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND column_name = 'updated_at'
), 'ALTER TABLE sw_model_instances ADD COLUMN updated_at DATETIME NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ============================================================
-- 3. 创建查询索引（MySQL 版本兼容写法）
-- ============================================================
SET @index_exists = (
	SELECT COUNT(*) FROM information_schema.statistics
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND index_name = 'ix_sw_model_instances_pid'
);
SET @sql = IF(@index_exists = 0, 'CREATE INDEX ix_sw_model_instances_pid ON sw_model_instances (pid_instance_id)', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @index_exists = (
	SELECT COUNT(*) FROM information_schema.statistics
	WHERE table_schema = DATABASE() AND table_name = 'sw_model_instances' AND index_name = 'ix_sw_model_instances_library'
);
SET @sql = IF(@index_exists = 0, 'CREATE INDEX ix_sw_model_instances_library ON sw_model_instances (library_item_id)', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ============================================================
-- 4. 升级结果检查
-- ============================================================
SELECT table_name, column_name, data_type
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'sw_model_instances'
ORDER BY ordinal_position;

SELECT table_name, index_name
FROM information_schema.statistics
WHERE table_schema = DATABASE()
  AND table_name = 'sw_model_instances'
  AND index_name IN ('PRIMARY', 'ix_sw_model_instances_pid', 'ix_sw_model_instances_library');
  */
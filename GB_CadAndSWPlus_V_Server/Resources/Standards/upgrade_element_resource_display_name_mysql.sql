-- 为已有 MySQL ELEMENT_RESOURCES 资源表增加 SolidWorks 模型按键显示名。
-- 可重复执行；不会修改 ELEMENT_DEFINITIONS.DISPLAY_NAME 或现有资源数据。
SET @resource_table_exists = (
	SELECT COUNT(*)
	FROM information_schema.tables
	WHERE table_schema = DATABASE() AND table_name = 'element_resources'
);

SET @display_name_exists = (
	SELECT COUNT(*)
	FROM information_schema.columns
	WHERE table_schema = DATABASE()
	  AND table_name = 'element_resources'
	  AND column_name = 'display_name'
);

SET @sql = IF(@resource_table_exists = 1 AND @display_name_exists = 0,
	'ALTER TABLE element_resources ADD COLUMN display_name VARCHAR(255) NULL AFTER resource_name',
	'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SELECT column_name, column_type, is_nullable
FROM information_schema.columns
WHERE table_schema = DATABASE()
  AND table_name = 'element_resources'
  AND column_name = 'display_name';

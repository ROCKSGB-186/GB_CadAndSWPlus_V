-- CAD PID 与 SolidWorks 实例关联表（MySQL）
-- 现有 CAD_FILE_STORAGE 和本地 Resources 不受影响。
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
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (sw_instance_id),
	KEY ix_sw_model_instances_pid (pid_instance_id),
	KEY ix_sw_model_instances_library (library_item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- CAD/SolidWorks 统一构件图元库一期数据库脚本：MySQL
-- 作用：新增统一构件定义和平台资源关联表。
-- 兼容原则：不修改、不删除 cad_file_storage、cad_block_attributes_json、sw_model_instances 及离线 Resources。
-- 本脚本不自动迁移旧数据；旧 CAD 图元将在后续绑定 API 中逐步关联。

CREATE TABLE IF NOT EXISTS element_definitions (
	id BIGINT NOT NULL,
	element_code VARCHAR(128) NOT NULL,
	name VARCHAR(255) NOT NULL,
	display_name VARCHAR(255) NULL,
	category_id INT NULL,
	element_type VARCHAR(64) NULL,
	discipline VARCHAR(64) NULL,
	standard_no VARCHAR(128) NULL,
	description VARCHAR(1000) NULL,
	status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
	current_version INT NOT NULL DEFAULT 1,
	created_by VARCHAR(128) NULL,
	updated_by VARCHAR(128) NULL,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_element_definitions_code (element_code),
	KEY ix_element_definitions_category (category_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS element_resources (
	id BIGINT NOT NULL,
	element_definition_id BIGINT NOT NULL,
	platform VARCHAR(32) NOT NULL,
	resource_type VARCHAR(32) NOT NULL,
	file_storage_id INT NULL,
	resource_name VARCHAR(255) NULL,
	resource_path VARCHAR(1000) NULL,
	file_hash VARCHAR(128) NULL,
	version INT NOT NULL DEFAULT 1,
	configuration_name VARCHAR(255) NULL,
	status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
	is_default INT NOT NULL DEFAULT 0,
	created_by VARCHAR(128) NULL,
	updated_by VARCHAR(128) NULL,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	CONSTRAINT fk_element_resources_definition
		FOREIGN KEY (element_definition_id) REFERENCES element_definitions(id),
	KEY ix_element_resources_element (element_definition_id, platform, status),
	KEY ix_element_resources_file (file_storage_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

SELECT table_name
FROM information_schema.tables
WHERE table_schema = DATABASE()
  AND table_name IN ('element_definitions', 'element_resources')
ORDER BY table_name;

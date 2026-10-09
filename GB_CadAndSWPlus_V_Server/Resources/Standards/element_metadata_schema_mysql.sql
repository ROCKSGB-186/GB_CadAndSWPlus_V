-- 统一构件属性与 SolidWorks 元数据一期脚本：MySQL
-- 依赖：element_library_schema_mysql.sql 已执行。
-- 兼容原则：不修改 cad_block_attributes_json；现有 CAD 属性继续保留。

CREATE TABLE IF NOT EXISTS element_property_definitions (
	id BIGINT NOT NULL,
	element_definition_id BIGINT NOT NULL,
	property_code VARCHAR(128) NOT NULL,
	property_name VARCHAR(255) NOT NULL,
	data_type VARCHAR(32) NOT NULL DEFAULT 'TEXT',
	unit VARCHAR(64) NULL,
	is_required INT NOT NULL DEFAULT 0,
	sort_order INT NOT NULL DEFAULT 0,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_element_property_code (element_definition_id, property_code),
	CONSTRAINT fk_element_property_element FOREIGN KEY (element_definition_id) REFERENCES element_definitions(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS element_property_values (
	id BIGINT NOT NULL,
	element_definition_id BIGINT NOT NULL,
	property_definition_id BIGINT NOT NULL,
	value_text VARCHAR(2000) NULL,
	value_number DECIMAL(20,6) NULL,
	value_bool INT NULL,
	source VARCHAR(32) NOT NULL DEFAULT 'MANUAL',
	version INT NOT NULL DEFAULT 1,
	created_at DATETIME NOT NULL,
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_element_property_value (element_definition_id, property_definition_id),
	CONSTRAINT fk_element_property_value_element FOREIGN KEY (element_definition_id) REFERENCES element_definitions(id),
	CONSTRAINT fk_element_property_value_def FOREIGN KEY (property_definition_id) REFERENCES element_property_definitions(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sw_element_definitions (
	element_definition_id BIGINT NOT NULL,
	model_type VARCHAR(32) NULL,
	default_configuration VARCHAR(255) NULL,
	coordinate_system_name VARCHAR(255) NULL,
	mate_rule_json TEXT NULL,
	port_count INT NOT NULL DEFAULT 0,
	model_status VARCHAR(32) NOT NULL DEFAULT 'DRAFT',
	updated_at DATETIME NOT NULL,
	PRIMARY KEY (element_definition_id),
	CONSTRAINT fk_sw_element_definition_element FOREIGN KEY (element_definition_id) REFERENCES element_definitions(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sw_connection_points (
	id BIGINT NOT NULL,
	element_definition_id BIGINT NOT NULL,
	point_code VARCHAR(128) NOT NULL,
	point_type VARCHAR(64) NULL,
	position_json VARCHAR(2000) NULL,
	direction_json VARCHAR(2000) NULL,
	diameter DECIMAL(20,6) NULL,
	unit VARCHAR(32) NULL,
	sort_order INT NOT NULL DEFAULT 0,
	PRIMARY KEY (id),
	UNIQUE KEY uk_sw_connection_point_code (element_definition_id, point_code),
	CONSTRAINT fk_sw_connection_point_element FOREIGN KEY (element_definition_id) REFERENCES element_definitions(id),
	KEY ix_sw_connection_points_element (element_definition_id, sort_order)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE INDEX ix_element_property_def_element
	ON element_property_definitions (element_definition_id, sort_order);

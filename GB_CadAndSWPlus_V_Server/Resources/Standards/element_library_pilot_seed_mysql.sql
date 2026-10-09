-- 统一构件图元库一期试点数据：MySQL
-- 依赖：element_library_schema_mysql.sql 已执行。
-- 说明：仅创建三个构件业务身份，不绑定文件、不修改旧 CAD/SW 资源。

INSERT INTO element_definitions
	(id, element_code, name, display_name, category_id, element_type, discipline, description, status, current_version, created_at, updated_at)
SELECT 1000001, 'VALVE-BFV-FLG-001', '蝶阀（法兰）', '蝶阀（法兰）', NULL, 'Valve', 'Process', '一期试点构件：法兰连接蝶阀。', 'DRAFT', 1, NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM element_definitions WHERE element_code = 'VALVE-BFV-FLG-001');

INSERT INTO element_definitions
	(id, element_code, name, display_name, category_id, element_type, discipline, description, status, current_version, created_at, updated_at)
SELECT 1000002, 'FLANGE-GENERAL-001', '法兰', '法兰', NULL, 'Flange', 'Process', '一期试点构件：通用法兰。', 'DRAFT', 1, NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM element_definitions WHERE element_code = 'FLANGE-GENERAL-001');

INSERT INTO element_definitions
	(id, element_code, name, display_name, category_id, element_type, discipline, description, status, current_version, created_at, updated_at)
SELECT 1000003, 'BLIND-PIPE-END-001', '管端盲板', '管端盲板', NULL, 'BlindPlate', 'Process', '一期试点构件：管端盲板。', 'DRAFT', 1, NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM element_definitions WHERE element_code = 'BLIND-PIPE-END-001');

SELECT id, element_code, name, category_id, status, current_version
FROM element_definitions
WHERE element_code IN ('VALVE-BFV-FLG-001', 'FLANGE-GENERAL-001', 'BLIND-PIPE-END-001')
ORDER BY id;

-- 统一登录服务端会话表（MySQL）
-- 执行前请先备份数据库，并确认当前数据库已切换到业务库。
-- token 只保存 SHA-256 摘要，客户端持有的原始 token 不写入数据库。

CREATE TABLE IF NOT EXISTS auth_sessions (
	id BIGINT NOT NULL AUTO_INCREMENT,
	user_id INT NOT NULL,
	token_hash VARCHAR(128) NOT NULL,
	client_platform VARCHAR(32) NOT NULL,
	created_at DATETIME NOT NULL,
	expires_at DATETIME NOT NULL,
	revoked_at DATETIME NULL,
	last_seen_at DATETIME NULL,
	PRIMARY KEY (id),
	UNIQUE KEY uk_auth_sessions_token_hash (token_hash),
	KEY ix_auth_sessions_user (user_id, revoked_at, expires_at),
	KEY ix_auth_sessions_expires (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

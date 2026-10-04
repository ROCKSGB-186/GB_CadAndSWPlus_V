# 04-client-server-consumers: 验证 CAD、Tray 与 Server 消费方

验证 CAD 主项目和 Tray 继续使用 net48，确认 CAD 主项目引用 Shared 的 net48 资产；验证 Server 保持 net8.0 并使用 Shared 的 net8.0 资产。处理评估发现的 Server 包兼容性问题，但不改变 Server 的目标框架或现有服务器行为。

**Done when**: CAD 主项目、Tray 和 Server 分别在预期目标框架下 restore/build，项目引用解析正确，未破坏本地离线资源、API 配置和服务器功能。

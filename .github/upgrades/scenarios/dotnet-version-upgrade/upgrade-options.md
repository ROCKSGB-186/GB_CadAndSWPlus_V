# Upgrade Options — GB_CadAndSWPlus_V

Assessment: 5 个 SDK-style 项目；当前包含 net48、net472 和 net8.0；客户端相关项目需要统一到 net48，Server 保留 net8.0。

## Strategy

### Upgrade Strategy
解决方案包含多个 .NET Framework 项目，按依赖关系逐层升级并在每层验证。

| Value | Description |
|-------|-------------|
| **Bottom-Up** (selected) | 先处理被依赖的基础库，再处理上层客户端和服务器项目，每层独立编译验证。 |

## Project Structure

### Project Approach
保留 Server 的 net8.0，以兼容现有 ASP.NET Core 服务；跨客户端与 Server 使用的共享库采用多目标框架，SolidWorks AddIn 和 Tray 等客户端项目统一到 net48。

| Value | Description |
|-------|-------------|
| **Multi-targeting** (selected) | Shared 等跨框架库同时支持 net48 和 net8.0；各应用项目保留适合自身运行环境的目标框架。 |
| In-place | 直接将所有需要变更的项目改为单一 net48，不保留 net8.0 Server。 |

### Package Management
由于 Shared 项目将同时面向 net48 和 net8.0，迁移期间保留各项目包配置，避免产生 VersionOverride 和分叉依赖问题。

| Value | Description |
|-------|-------------|
| **Per-Project (defer CPM to post-migration)** (selected) | 保留各项目 NuGet 配置，待多目标迁移稳定后再评估集中包管理。 |
| Central Package Management | 创建 Directory.Packages.props，集中维护 NuGet 版本；多目标过渡期间可能增加版本覆盖复杂度。 |

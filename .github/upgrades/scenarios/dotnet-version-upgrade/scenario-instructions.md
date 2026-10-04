# .NET Framework 版本统一

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: net48（.NET Framework 4.8）
- **Scope**: CAD 客户端与共享库；Server 保留 net8.0
- **Reason**: 以 CAD 主项目当前采用的 net48 为客户端统一目标，同时保持服务器功能可用
- **Commit Strategy**: After Each Task

## Source Control
- **Source Branch**: main
- **Working Branch**: upgrade-dotnet-48
- **Pending Changes**: 已在开始前提交保存
- **Commit Strategy**: After Each Task
- **Branch Sync**: Disabled

## Upgrade Options
**Source**: .github/upgrades/scenarios/dotnet-version-upgrade/upgrade-options.md

### Strategy
- Upgrade Strategy: Bottom-Up

### Project Structure
- Project Approach: Multi-targeting
- Package Management: Per-Project (defer CPM to post-migration)

## Decisions
- 保留 GB_CadAndSWPlus_V_Server 的 net8.0，避免破坏现有 ASP.NET Core 服务器功能。
- 对客户端与 Server 共同引用的 Shared 项目采用 net48;net8.0 多目标框架。

## Strategy
**Selected**: Bottom-Up (Dependency-First)
**Rationale**: 解决方案包含多个 .NET Framework 项目和跨框架共享依赖；先稳定叶子库，再验证上层项目。

### Execution Constraints
- 严格按任务顺序执行；前一层 restore、build 和测试通过后才能进入下一层。
- Shared 多目标必须同时验证 net48 与 net8.0，不得仅验证单一目标。
- Server 保持 net8.0，不得为追求统一而改成 net48。
- 每个层级完成后验证上层项目仍可构建，并保留本地 Resources、CAD/SolidWorks 功能和中文日志。
- 多目标迁移期间保留各项目 NuGet 配置，Central Package Management 仅作为后续稳定后的建议。

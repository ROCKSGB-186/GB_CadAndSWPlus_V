# .NET Framework 版本统一计划

## Overview

**Target**: 将 CAD 客户端相关项目统一到 .NET Framework 4.8，同时保留 Server 的 net8.0，并让跨项目共享库支持 net48 与 net8.0。
**Scope**: 5 个项目；其中 3 个项目需要框架配置调整，2 个项目保持现状。

### Selected Strategy
**Bottom-Up (Dependency-First)** — Upgrade from leaf nodes to root applications, tier by tier.
**Rationale**: 解决方案包含 5 个项目和跨 net48/net8.0 的共享依赖，需要先稳定共享基础库，再验证上层客户端和 Server。

### Dependency Graph

```text
Tier 1: GB_CadAndSWPlus_V_Shared, GB_CadAndSWPlus_V_SolidWorksAddIn, GB_CadAndSWPlus_V_Tray, GB_CadAndSWPlus_V_Server
		   ↓
Tier 2: GB_CadAndSWPlus_V
```

Shared 被 CAD 主项目引用；Server 保持 net8.0，Shared 将提供 net8.0 目标以维持兼容。

## Tasks

### 01-prerequisites: 验证工具链并建立基线

确认 net48 开发者包、.NET 8 SDK、NuGet restore 能力和解决方案当前状态，并对未改动的基线执行编译。重点记录现有 Server 的 restore/build 结果以及所有项目的目标框架，避免将既有问题误判为迁移回归。

**Done when**: 工具链检查完成，基线编译结果已记录，未发现循环项目引用，执行约束和项目范围明确。

---

### 02-shared-library: 将共享库改为多目标框架

调整 `GB_CadAndSWPlus_V_Shared`，使其同时支持 `net48` 和 `net8.0`，供 CAD/SolidWorks 客户端与 Server 分别引用正确资产。研究其 WPF/API 使用、项目引用和 NuGet 依赖；多目标期间保留按项目管理包版本，不创建 Directory.Packages.props。

**Done when**: Shared 在 net48 和 net8.0 两个目标下均能 restore/build，公共 API 在两目标下保持一致或已记录必要条件编译，相关测试通过。

---

### 03-solidworks-client: 将 SolidWorks AddIn 统一到 net48

将 `GB_CadAndSWPlus_V_SolidWorksAddIn` 从 net472 调整为 net48，保持 SolidWorks 2022 互操作引用、离线 Resources、本地 DWG 功能、中文日志和现有插件加载行为。确认其对 Shared 的引用与 net48 目标匹配，并修复编译期兼容问题。

**Done when**: SolidWorks AddIn 以 net48 成功 restore/build，SolidWorks 相关引用无冲突，原有本地资源和日志代码仍被项目包含。

---

### 04-client-server-consumers: 验证 CAD、Tray 与 Server 消费方

验证 CAD 主项目和 Tray 继续使用 net48，确认 CAD 主项目引用 Shared 的 net48 资产；验证 Server 保持 net8.0 并使用 Shared 的 net8.0 资产。处理评估发现的 Server 包兼容性问题，但不改变 Server 的目标框架或现有服务器行为。

**Done when**: CAD 主项目、Tray 和 Server 分别在预期目标框架下 restore/build，项目引用解析正确，未破坏本地离线资源、API 配置和服务器功能。

---

### 05-solution-validation: 执行全解决方案验证

执行完整解决方案 restore/build 和可用测试，检查所有项目的目标框架、输出目录、依赖资产及运行时配置。记录多目标迁移结果，并将 Central Package Management 作为后续稳定后的建议，而不是本次迁移的强制改动。

**Done when**: 全解决方案在允许的项目目标下编译成功，测试通过或明确记录既有失败，所有框架差异和延期事项已写入验证记录。

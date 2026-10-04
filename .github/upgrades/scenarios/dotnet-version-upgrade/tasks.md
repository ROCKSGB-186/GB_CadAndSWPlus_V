# .NET Framework 版本统一进度

## Overview

将 CAD 客户端相关项目统一到 .NET Framework 4.8，保留 Server 的 net8.0，并将 Shared 调整为 net48/net8.0 多目标框架。采用依赖优先策略，逐步验证每个层级。

**Progress**: 1/5 tasks complete <progress value="20" max="100"></progress> 20%

## Tasks

- ✅ 01-prerequisites: 验证工具链并建立基线 ([Content](tasks/01-prerequisites/task.md), [Progress](tasks/01-prerequisites/progress-details.md))
- 🔲 02-shared-library: 将共享库改为多目标框架
- 🔲 03-solidworks-client: 将 SolidWorks AddIn 统一到 net48
- 🔲 04-client-server-consumers: 验证 CAD、Tray 与 Server 消费方
- 🔲 05-solution-validation: 执行全解决方案验证

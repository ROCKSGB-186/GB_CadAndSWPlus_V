# 01-prerequisites: 验证工具链并建立基线

确认 net48 开发者包、.NET 8 SDK、NuGet restore 能力和解决方案当前状态，并对未改动的基线执行编译。重点记录现有 Server 的 restore/build 结果以及所有项目的目标框架，避免将既有问题误判为迁移回归。

**Done when**: 工具链检查完成，基线编译结果已记录，未发现循环项目引用，执行约束和项目范围明确。

## Research Findings

- 解决方案文件：`GB_CadAndSWPlus_V/GB_CadAndSWPlus_V.sln`。
- 项目目标框架：CAD 主项目 `net48`；Tray `net48`；SolidWorks AddIn `net472`；Shared `net472`；Server `net8.0`。
- 项目格式：评估报告显示 5 个项目均为 SDK-style；未发现需要 SDK-style 转换的项目。
- 已知项目引用：CAD 主项目引用 Shared；Shared 将需要同时生成 net48 和 net8.0 资产，以保留 Server 的 net8.0 消费能力。
- 用户确认范围：SolidWorks AddIn 迁移到 net48；Shared 采用 `net48;net8.0`；CAD 主项目和 Tray 保持 net48；Server 保持 net8.0。
- NuGet 策略：多目标迁移期间保留各项目 PackageReference，不创建 Directory.Packages.props；Central Package Management 延后评估。
- 评估结果：共 5 个项目，3 个项目需要框架配置调整；报告显示 Server 存在 1 个 NuGet 兼容性问题，需在实际 restore/build 中确认。
- 构建工具决策：项目包含 WPF、WinForms、XAML、嵌入式资源和 .NET Framework 目标，优先使用 Visual Studio MSBuild；后续多目标 Shared 按 `net48` 与 `net8.0` 分别验证。
- 约束：不得删除 Resources 中的本地图元离线路径；不得削弱 CAD/SolidWorks 中文操作日志和 Server API 配置逻辑。

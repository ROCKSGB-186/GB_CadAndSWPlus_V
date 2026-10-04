# 03-solidworks-client: 将 SolidWorks AddIn 统一到 net48

将 `GB_CadAndSWPlus_V_SolidWorksAddIn` 从 net472 调整为 net48，保持 SolidWorks 2022 互操作引用、离线 Resources、本地 DWG 功能、中文日志和现有插件加载行为。确认其对 Shared 的引用与 net48 目标匹配，并修复编译期兼容问题。

**Done when**: SolidWorks AddIn 以 net48 成功 restore/build，SolidWorks 相关引用无冲突，原有本地资源和日志代码仍被项目包含。

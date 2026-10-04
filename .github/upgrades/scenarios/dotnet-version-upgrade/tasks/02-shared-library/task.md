# 02-shared-library: 将共享库改为多目标框架

调整 `GB_CadAndSWPlus_V_Shared`，使其同时支持 `net48` 和 `net8.0`，供 CAD/SolidWorks 客户端与 Server 分别引用正确资产。研究其 WPF/API 使用、项目引用和 NuGet 依赖；多目标期间保留按项目管理包版本，不创建 Directory.Packages.props。

**Done when**: Shared 在 net48 和 net8.0 两个目标下均能 restore/build，公共 API 在两目标下保持一致或已记录必要条件编译，相关测试通过。

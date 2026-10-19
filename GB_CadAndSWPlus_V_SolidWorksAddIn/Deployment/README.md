# SolidWorks 插件安装包

本目录用于生成不依赖 Visual Studio 的 SolidWorks 插件 EXE 安装包。

## 目标电脑要求

- 已安装 SolidWorks（安装程序会自动查找 SolidWorks API 目录）
- .NET Framework 4.7.2 或更高版本
- Microsoft Edge WebView2 Runtime
- 安装程序需要管理员权限，以便注册 COM 插件

安装程序不会安装 SolidWorks 本体，也不会复制 SolidWorks API DLL。API DLL 必须由目标电脑上的 SolidWorks 提供。

## 发布目录

先在开发电脑构建插件项目：

- 配置：Release
- 平台：x64
- 项目：`GB_CadAndSWPlus_V_SolidWorksAddIn`

然后将插件输出目录整体复制为：

```text
GB_CadAndSWPlus_V_SolidWorksAddIn/Deployment/Payload/
```

必须保留输出目录中的 DLL、配置文件以及 `DisplayPages` 下的 HTML、three、Icons 等资源。

## 生成安装包

1. 安装 Inno Setup 6。
2. 在仓库根目录运行：

```powershell
iscc "GB_CadAndSWPlus_V_SolidWorksAddIn\Deployment\GB_CadAndSWPlus_V_SolidWorksAddIn.iss"
```

3. 安装包输出到：

```text
GB_CadAndSWPlus_V_SolidWorksAddIn/Deployment/Output/
```

如需让安装包自动离线安装 WebView2，请将微软官方 x64 Evergreen Standalone Installer 放置为：

```text
GB_CadAndSWPlus_V_SolidWorksAddIn/Deployment/Prerequisites/MicrosoftEdgeWebView2RuntimeInstallerX64.exe
```

安装包不会打包或分发 SolidWorks API DLL；这些 DLL 必须来自目标电脑已安装的 SolidWorks。安装器会搜索常见的 SolidWorks 安装目录并拒绝在未检测到 SolidWorks 时继续安装。

## 安装行为

安装程序会：

1. 检查 Windows 64 位和管理员权限；
2. 检查 .NET Framework 4.7.2；
3. 检测 SolidWorks 安装版本和 API 目录；
4. 复制插件文件到 `C:\Program Files\GB_CADPLUS\SolidWorksAddIn`；
5. 使用 64 位 `RegAsm.exe /codebase` 注册插件；
6. 创建开始菜单中的“手动注册 SolidWorks 插件”和“卸载插件”入口。

WebView2 Runtime 不能安全地随插件复制，安装程序会提示下载/安装；也可以先离线安装 Evergreen WebView2 Runtime 后再运行本安装包。

## 手动注册

管理员 PowerShell：

```powershell
$regasm = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
$dll = "C:\Program Files\GB_CADPLUS\SolidWorksAddIn\GB_CadAndSWPlus_V_SolidWorksAddIn.dll"
& $regasm $dll /codebase
```

如果安装目录不是默认目录，请把 `$dll` 改成实际 DLL 路径。注销插件：

```powershell
& $regasm $dll /unregister
```

执行注册或注销前必须关闭 SolidWorks。安装器生成前，必须先在开发电脑构建 `Release/x64` 插件并把整个输出目录复制到 `Deployment/Payload`，不能只复制主 DLL。

卸载前请关闭 SolidWorks，否则插件 DLL 可能被占用。

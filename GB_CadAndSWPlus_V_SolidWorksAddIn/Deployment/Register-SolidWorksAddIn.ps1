$ErrorActionPreference = 'Stop'
$dll = Join-Path $PSScriptRoot '..\GB_CadAndSWPlus_V_SolidWorksAddIn.dll'
$dll = [IO.Path]::GetFullPath($dll)
$regAsm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'

if (-not [Environment]::Is64BitOperatingSystem) { throw '当前系统不是 64 位 Windows。' }
if (-not (Test-Path $dll)) { throw "插件 DLL 不存在：$dll" }
if (-not (Test-Path $regAsm)) { throw "未找到 64 位 RegAsm：$regAsm" }

$running = Get-Process -Name 'SLDWORKS' -ErrorAction SilentlyContinue
if ($running) { throw '请先关闭 SolidWorks，再执行插件注册。' }

& $regAsm $dll /codebase
if ($LASTEXITCODE -ne 0) { throw "SolidWorks 插件注册失败，退出码：$LASTEXITCODE" }
Write-Host 'SolidWorks 插件注册成功。'

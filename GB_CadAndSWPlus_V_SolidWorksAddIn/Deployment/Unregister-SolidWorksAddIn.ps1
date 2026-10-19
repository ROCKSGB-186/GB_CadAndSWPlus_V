$ErrorActionPreference = 'Stop'
$dll = Join-Path $PSScriptRoot '..\GB_CadAndSWPlus_V_SolidWorksAddIn.dll'
$dll = [IO.Path]::GetFullPath($dll)
$regAsm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'

if ((Test-Path $dll) -and (Test-Path $regAsm)) {
	& $regAsm $dll /unregister
	if ($LASTEXITCODE -ne 0) { Write-Warning "插件注销返回退出码：$LASTEXITCODE" }
}

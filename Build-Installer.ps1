$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Write-Host 'Publishing Saab 93 NG Led Enabler (win-x86, self-contained)...'
dotnet publish .\SaabDidTool.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\win-x86
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
$compiler = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $compiler) { throw 'Install Inno Setup 6, then run ISCC.exe Saab93NGLedEnabler.iss' }
& $compiler .\Saab93NGLedEnabler.iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed' }
Write-Host 'Installer ready in InstallerOutput\'

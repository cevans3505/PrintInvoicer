# Builds PrintInvoicer as a single self-contained .exe, installs it for the current user and adds a Start menu shortcut.
# Works in Windows PowerShell 5.1. Close PrintInvoicer before running it.
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\PrintInvoicer')
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish'

if (Get-Process PrintInvoicer -ErrorAction SilentlyContinue) {
    throw 'PrintInvoicer is running. Close it and run this again.'
}

& dotnet test (Join-Path $root 'tests\PrintInvoicer.Tests') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not publishing.' }

if (Test-Path $out) { Remove-Item (Join-Path $out '*') -Recurse -Force }
& dotnet publish (Join-Path $root 'src\PrintInvoicer.App') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $out '*') $InstallDir -Recurse -Force

$exe = Join-Path $InstallDir 'PrintInvoicer.exe'
$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'PrintInvoicer.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($lnk)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = '3D print job invoicing'
$shortcut.Save()

# Ask Explorer to refresh its icon cache so the shortcut picks up icon changes.
Start-Process -FilePath "$env:SystemRoot\System32\ie4uinit.exe" -ArgumentList '-show' -Wait -ErrorAction SilentlyContinue

Write-Host "Installed $exe"
Write-Host "Start menu shortcut: $lnk"

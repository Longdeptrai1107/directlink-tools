$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path $compiler)) { throw 'Khong tim thay C# compiler cua .NET Framework.' }
$output = Join-Path $PSScriptRoot 'release'
$assets = Join-Path $PSScriptRoot '.build'
New-Item -ItemType Directory -Force -Path $output, $assets | Out-Null
$icon = Join-Path $assets 'DirectLink.ico'
& (Join-Path $PSScriptRoot 'CreateIcon.ps1') -OutputPath $icon
$sources = @('Program.cs', 'MainForm.cs', 'Theme.cs', 'Downloader.cs', 'DownloadPaths.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 "/out:$output\DirectLinkDownloader.exe" "/win32icon:$icon" "/win32manifest:$PSScriptRoot\App.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Single-file EXE: $output\DirectLinkDownloader.exe"

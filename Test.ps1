$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$output = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& $compiler /nologo /target:exe /optimize+ /codepage:65001 "/out:$output\DownloaderTests.exe" /reference:System.dll /reference:System.Core.dll (Join-Path $PSScriptRoot 'Downloader.cs') (Join-Path $PSScriptRoot 'DownloadPaths.cs') (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $output 'DownloaderTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

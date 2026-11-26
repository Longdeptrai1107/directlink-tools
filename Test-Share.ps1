param([switch]$Internet, [switch]$ExpectBlocked)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'test-output\share'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release-share\DirectLinkShare.exe') -Destination (Join-Path $output 'DirectLinkShare.exe') -Force
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 "/out:$output\ShareTests.exe" /reference:System.dll /reference:System.Core.dll "/reference:$output\DirectLinkShare.exe" (Join-Path $PSScriptRoot 'Share\Tests.cs') (Join-Path $PSScriptRoot 'Downloader.cs') (Join-Path $PSScriptRoot 'DownloadPaths.cs')
if ($LASTEXITCODE -ne 0) { throw 'Share test compilation failed.' }
if ($ExpectBlocked) { & (Join-Path $output 'ShareTests.exe') --expect-blocked }
elseif ($Internet) { & (Join-Path $output 'ShareTests.exe') --internet }
else { & (Join-Path $output 'ShareTests.exe') }
if ($LASTEXITCODE -ne 0) { throw 'Share tests failed.' }

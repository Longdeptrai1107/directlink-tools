$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'test-output\share-ui'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release-share\DirectLinkShare.exe') -Destination (Join-Path $output 'DirectLinkShare.exe') -Force
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 "/out:$output\ShareUiTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$output\DirectLinkShare.exe" (Join-Path $PSScriptRoot 'Share\UiTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Share UI test compilation failed.' }
& (Join-Path $output 'ShareUiTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Share UI tests failed.' }

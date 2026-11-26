$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$output = Join-Path $PSScriptRoot 'test-output\ui-workflow'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release\DirectLinkDownloader.exe') -Destination (Join-Path $output 'DirectLinkDownloader.exe') -Force
& $compiler /nologo /target:exe /main:UiWorkflowTests /optimize+ /codepage:65001 "/out:$output\UiWorkflowTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "/reference:$output\DirectLinkDownloader.exe" (Join-Path $PSScriptRoot 'UiWorkflowTests.cs') (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
& (Join-Path $output 'UiWorkflowTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'UI workflow tests failed.' }

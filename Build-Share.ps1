$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) { throw 'Build DirectLink Share requires 64-bit Windows with .NET Framework.' }
$assets = Join-Path $PSScriptRoot '.build\share'
$output = Join-Path $PSScriptRoot 'release-share'
New-Item -ItemType Directory -Path $assets, $output -Force | Out-Null
$version = '2026.11.1'
$hash = '341deda3db5ec498a0482254d6c38c0b7cf01a30c37bb5dd95d2216d08955e35'
$binary = Join-Path $assets "cloudflared-$version-windows-amd64.exe"
if (!(Test-Path -LiteralPath $binary)) {
    & curl.exe --fail --location --silent --show-error --retry 2 --output $binary "https://github.com/cloudflare/cloudflared/releases/download/$version/cloudflared-windows-amd64.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Cloudflared download failed.' }
}
if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw 'Official cloudflared SHA-256 mismatch.' }
$license = Join-Path $assets 'cloudflared-LICENSE.txt'
if (!(Test-Path -LiteralPath $license)) {
    & curl.exe --fail --location --silent --show-error --output $license "https://raw.githubusercontent.com/cloudflare/cloudflared/$version/LICENSE"
    if ($LASTEXITCODE -ne 0) { throw 'Cloudflared license download failed.' }
}
$compressed = Join-Path $assets 'cloudflared.gz'
if (!(Test-Path -LiteralPath $compressed) -or (Get-Item -LiteralPath $compressed).LastWriteTimeUtc -lt (Get-Item -LiteralPath $binary).LastWriteTimeUtc) {
    $inputFile = [IO.File]::OpenRead($binary)
    try {
        $outputFile = [IO.File]::Create($compressed)
        try {
            $gzip = New-Object IO.Compression.GZipStream($outputFile, [IO.Compression.CompressionMode]::Compress)
            try { $inputFile.CopyTo($gzip) } finally { $gzip.Dispose() }
        } finally { $outputFile.Dispose() }
    } finally { $inputFile.Dispose() }
}
$icon = Join-Path $assets 'Share.ico'
& (Join-Path $PSScriptRoot 'CreateIcon.ps1') -OutputPath $icon -Upload
$sources = @('Share\Program.cs','Share\ShareForm.cs','Share\LocalFileServer.cs','Share\TunnelProcess.cs','Share\ShareSession.cs','Theme.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /nowarn:0649 /utf8output /codepage:65001 "/out:$output\DirectLinkShare.exe" "/win32icon:$icon" "/win32manifest:$PSScriptRoot\Share\App.manifest" "/resource:$compressed,DirectLinkShare.cloudflared.gz" "/resource:$license,DirectLinkShare.cloudflared.LICENSE" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Share build failed.' }
Write-Output ('Single-file app: ' + (Join-Path $output 'DirectLinkShare.exe'))
Write-Output ('Embedded cloudflared version: ' + $version)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$exe = Join-Path $PSScriptRoot 'release\DirectLinkDownloader.exe'
[void][Reflection.Assembly]::LoadFrom($exe)
[System.Windows.Forms.Application]::EnableVisualStyles()
$output = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$form = New-Object DirectLinkDownloader.MainForm
try {
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $image = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
    try {
        $form.DrawToBitmap($image, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
        $image.Save((Join-Path $output 'interface-v2.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $image.Dispose() }
    Write-Output ('PASS main window rendered: ' + $form.Width + 'x' + $form.Height)
    $form.ClientSize = New-Object System.Drawing.Size(1000, 720)
    [System.Windows.Forms.Application]::DoEvents()
    $small = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
    try {
        $form.DrawToBitmap($small, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
        $small.Save((Join-Path $output 'interface-v2-small.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $small.Dispose() }
    Write-Output 'PASS compact window rendered'
    Write-Output ('Downloads folder: ' + [DirectLinkDownloader.DownloadPaths]::GetDownloadsFolder())
} finally {
    $form.Close()
    $form.Dispose()
}

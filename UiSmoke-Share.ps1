$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'release-share\DirectLinkShare.exe'))
[System.Windows.Forms.Application]::EnableVisualStyles()
$output = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$form = New-Object DirectLinkShare.ShareForm
try {
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $bitmap = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
    try {
        $form.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
        $bitmap.Save((Join-Path $output 'share-interface.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    $form.ClientSize = New-Object System.Drawing.Size(1000, 720)
    [System.Windows.Forms.Application]::DoEvents()
    $small = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
    try {
        $form.DrawToBitmap($small, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
        $small.Save((Join-Path $output 'share-interface-small.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $small.Dispose() }
    Write-Output 'PASS share UI rendered at full and compact window sizes'
} finally { $form.Close(); $form.Dispose() }

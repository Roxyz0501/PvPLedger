Add-Type -AssemblyName System.Drawing
$iconDirectory = Join-Path $PSScriptRoot '../PvPLedger/images'
New-Item -ItemType Directory -Force $iconDirectory | Out-Null
$bitmap = [System.Drawing.Bitmap]::new(512, 512)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(10, 17, 27))
$mint = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(102, 221, 202))
$muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(68, 93, 115))
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(230, 239, 242))
$rose = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(237, 145, 163))
$pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(102, 221, 202), 12)
$g.DrawRectangle($pen, 100, 82, 312, 350)
$g.FillRectangle($mint, 136, 124, 130, 12)
$g.FillRectangle($muted, 136, 160, 240, 6)
$g.FillRectangle($mint, 144, 240, 52, 118)
$g.FillRectangle($rose, 230, 288, 52, 70)
$g.FillRectangle($white, 316, 202, 52, 156)
$g.FillRectangle($muted, 136, 382, 240, 6)
$bitmap.Save((Join-Path $iconDirectory 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$pen.Dispose(); $mint.Dispose(); $muted.Dispose(); $white.Dispose(); $rose.Dispose(); $g.Dispose(); $bitmap.Dispose()

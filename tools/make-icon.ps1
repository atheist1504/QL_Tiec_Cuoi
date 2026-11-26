# Tạo icon ứng dụng (.ico nhiều kích thước) từ một ảnh PNG.
# Chạy: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -Source <ảnh.png> -OutDir src\QLTiecCuoi.Desktop\Assets
param(
    [Parameter(Mandatory)] [string]$Source,
    [Parameter(Mandatory)] [string]$OutDir
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
try {
    # Đặt ảnh vào khung vuông nền đen (giữ tỉ lệ, căn giữa) để icon không bị méo.
    $side = [Math]::Max($src.Width, $src.Height)
    $square = New-Object System.Drawing.Bitmap $side, $side
    $g = [System.Drawing.Graphics]::FromImage($square)
    $g.Clear([System.Drawing.Color]::Black)
    $g.DrawImage($src, [int](($side - $src.Width) / 2), [int](($side - $src.Height) / 2), $src.Width, $src.Height)
    $g.Dispose()
} finally { $src.Dispose() }

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($square, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'app.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}
$square.Dispose()

# Ghi file ICO: header 6 byte + mỗi ảnh 16 byte mô tả + dữ liệu PNG.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $pngs[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }   # 0 nghĩa là 256
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $w.Write([byte[]]$data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path $OutDir) 'app.ico'), $out.ToArray())
"Đã tạo app.ico ($($sizes -join ', ') px) và app.png trong $OutDir"

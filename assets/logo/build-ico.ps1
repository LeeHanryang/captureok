# 로고 PNG로 앱 아이콘(.ico)을 만든다: 16·32px는 손으로 다듬은 그림을 그대로, 나머지는 512px에서 줄인다.
# 64px 이하는 BMP 항목(오래된 프로그램도 읽음), 256px만 PNG 항목으로 넣는다.
# 결과: src\CaptureOk\Assets\CaptureOk.ico (실행 파일·창·트레이·설치 파일 아이콘)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$out = Join-Path (Split-Path (Split-Path $here -Parent) -Parent) 'src\CaptureOk\Assets\CaptureOk.ico'
$master = [System.Drawing.Image]::FromFile((Join-Path $here 'icon-512.png'))

function Picture([int]$size) {
    $ready = Join-Path $here "icon-$size.png"
    if (Test-Path $ready) {
        $src = [System.Drawing.Image]::FromFile($ready)
        $bmp = New-Object System.Drawing.Bitmap $src
        $src.Dispose()
        return $bmp
    }
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($master, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function PngEntry($bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray() # 쉼표: 바이트 배열이 파이프라인에서 풀리지 않게
}

# 32비트 DIB: BITMAPINFOHEADER(높이는 그림+마스크라 두 배) + 아래에서 위로 BGRA + 1비트 AND 마스크(알파를 쓰므로 0)
function DibEntry($bmp) {
    $size = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($size * $size * 4)
    for ($y = 0; $y -lt $size; $y++) {
        [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $pixels, ($size - 1 - $y) * $size * 4, $size * 4)
    }
    $bmp.UnlockBits($data)
    $maskRow = [int]([Math]::Ceiling($size / 32) * 4)
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$size); $w.Write([Int32]($size * 2))
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]($pixels.Length + $maskRow * $size)); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    $w.Write($pixels)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bmp = Picture $size
    if ($size -ge 256) { $images.Add((PngEntry $bmp)) } else { $images.Add((DibEntry $bmp)) }
    $bmp.Dispose()
}
$master.Dispose()

# ICO: 머리 6바이트 + 항목 16바이트씩 + 그림 데이터
$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]; $data = $images[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Flush()
[IO.File]::WriteAllBytes($out, $ms.ToArray())
Get-Item $out

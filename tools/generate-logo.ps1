# Pulpit logo generator â€” red cross over an open bible, redrawn as vectors on a 512 canvas.
Add-Type -AssemblyName System.Drawing

$red   = [System.Drawing.Color]::FromArgb(255, 158, 27, 32)    # æ·±ç –çº¢,å–è‡ªç…§ç‰‡
$beige = [System.Drawing.Color]::FromArgb(255, 227, 220, 203)  # é¢„è§ˆåº•è‰²,å–è‡ªç…§ç‰‡

function Draw-Mark {
    param([System.Drawing.Graphics]$g)

    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $brush = New-Object System.Drawing.SolidBrush($red)

    # ---- åå­—:ç«–æ†(ä¸¤ç«¯å¤–æ‰©) ----
    $v = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ptsV = @(
        (New-Object System.Drawing.PointF(222, 48)),  # é¡¶å·¦
        (New-Object System.Drawing.PointF(290, 48)),  # é¡¶å³
        (New-Object System.Drawing.PointF(276, 94)),  # å³è…°ä¸Š
        (New-Object System.Drawing.PointF(276, 340)), # å³è…°ä¸‹
        (New-Object System.Drawing.PointF(289, 438)), # åº•å³
        (New-Object System.Drawing.PointF(223, 438)), # åº•å·¦
        (New-Object System.Drawing.PointF(236, 340)), # å·¦è…°ä¸‹
        (New-Object System.Drawing.PointF(236, 94))   # å·¦è…°ä¸Š
    )
    $v.AddPolygon($ptsV)
    $g.FillPath($brush, $v)

    # ---- åå­—:æ¨ªæ†(ä¸¤ç«¯å¤–æ‰©) ----
    $h = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ptsH = @(
        (New-Object System.Drawing.PointF(116, 128)),
        (New-Object System.Drawing.PointF(162, 144)),
        (New-Object System.Drawing.PointF(350, 144)),
        (New-Object System.Drawing.PointF(396, 128)),
        (New-Object System.Drawing.PointF(396, 196)),
        (New-Object System.Drawing.PointF(350, 180)),
        (New-Object System.Drawing.PointF(162, 180)),
        (New-Object System.Drawing.PointF(116, 196))
    )
    $h.AddPolygon($ptsH)
    $g.FillPath($brush, $h)

    # ---- ç¿»å¼€çš„åœ£ç»:å·¦ç¿¼(ç¼Žå¸¦çŠ¶,å†…é«˜å¤–ä½Ž,å¤–å°–ä¸‹æ‘†) ----
    $wl = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wl.StartFigure()
    # é¡¶è¾¹:å¤–å°– â†’ æ³¢æµª â†’ å†…ç«¯ä¸Šè§’(ä¹¦è„Šä¾§æœ€é«˜)
    $wl.AddBezier(30, 428, 68, 398, 100, 386, 142, 384)
    $wl.AddBezier(142, 384, 178, 382, 196, 364, 204, 346)
    # å†…ç«¯æ–œåˆ‡
    $wl.AddLine(204, 346, 216, 392)
    # åº•è¾¹:å¤§è‡´ç­‰åŽšåœ°æ‰«å›žå¤–å°–
    $wl.AddBezier(216, 392, 152, 416, 84, 438, 30, 428)
    $wl.CloseFigure()
    $g.FillPath($brush, $wl)

    # ---- å³ç¿¼(é•œåƒ) ----
    $wr = New-Object System.Drawing.Drawing2D.GraphicsPath
    $wr.StartFigure()
    $wr.AddBezier(482, 428, 444, 398, 412, 386, 370, 384)
    $wr.AddBezier(370, 384, 334, 382, 316, 364, 308, 346)
    $wr.AddLine(308, 346, 296, 392)
    $wr.AddBezier(296, 392, 360, 416, 428, 438, 482, 428)
    $wr.CloseFigure()
    $g.FillPath($brush, $wr)

    $brush.Dispose()
}

$out  = $env:TEMP
$repo = Split-Path $PSScriptRoot -Parent

# é¢„è§ˆç¨¿:ç±³è‰²åº•
$bmp = New-Object System.Drawing.Bitmap(512, 512)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear($beige)
Draw-Mark $g
$g.Dispose()
$bmp.Save("$out\logo-preview.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# æ¯ç‰ˆ:é€æ˜Žåº• 512
$master = New-Object System.Drawing.Bitmap(512, 512)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.Clear([System.Drawing.Color]::Transparent)
Draw-Mark $g
$g.Dispose()

# æ–‡æ¡£ç”¨:ç±³è‰²åº• 512 â†’ docs/logo.png
New-Item -ItemType Directory -Force "$repo\docs" | Out-Null
$docBmp = New-Object System.Drawing.Bitmap(512, 512)
$g = [System.Drawing.Graphics]::FromImage($docBmp)
$g.Clear($beige)
Draw-Mark $g
$g.Dispose()
$docBmp.Save("$repo\docs\logo.png", [System.Drawing.Imaging.ImageFormat]::Png)
$docBmp.Dispose()

# ICO:PNG åŽ‹ç¼©æ¡ç›®(Vista+ å‡æ”¯æŒ),å°ºå¯¸ 256/64/48/32/16,é€æ˜Žåº•
function Get-PngBytes([System.Drawing.Bitmap]$src, [int]$size) {
    $b = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    # å‰ç½®é€—å·é˜²æ­¢ PowerShell æŠŠ byte[] å±•å¼€è¿›ç®¡é“
    return ,$ms.ToArray()
}

$sizes = @(256, 64, 48, 32, 16)
$blobs = @()
foreach ($s in $sizes) { $blobs += ,(Get-PngBytes $master $s) }
$master.Dispose()

New-Item -ItemType Directory -Force "$repo\src\Pulpit.App\Assets" | Out-Null
$icoPath = "$repo\src\Pulpit.App\Assets\pulpit.ico"
$fs = [System.IO.File]::Create($icoPath)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }                               # 0 = 256
    $w.Write([Byte]$dim); $w.Write([Byte]$dim)                             # å®½ é«˜
    $w.Write([Byte]0); $w.Write([Byte]0)                                   # è‰²æ¿ ä¿ç•™
    $w.Write([UInt16]1); $w.Write([UInt16]32)                              # planes bpp
    $w.Write([UInt32]([byte[]]$blobs[$i]).Length); $w.Write([UInt32]$offset)
    $offset += ([byte[]]$blobs[$i]).Length
}
foreach ($blob in $blobs) { $w.Write([byte[]]$blob) }
$w.Dispose(); $fs.Dispose()

"assets written:"
Get-Item "$repo\docs\logo.png", $icoPath | Select-Object FullName, Length


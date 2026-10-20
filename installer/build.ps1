# Builds artifacts\ClientSSH-Setup-<version>.exe. Needs the .NET 8 SDK and Inno Setup 6 (ISCC.exe).
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$out = Join-Path $root "artifacts"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

# Unpackaged, self-contained build (the Setup.exe replaces MSIX). Trimming is off: it breaks WinUI/XAML reflection.
dotnet publish "$root\ClientSSH.csproj" -c Release -r win-x64 -p:Platform=x64 `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true `
    -p:PublishTrimmed=false -p:Version=$Version -p:PublishProfile= -o "$out\publish"
if ($LASTEXITCODE) { throw "publish failed" }

# Wizard art from the brand kit: dark panel with the logo (side) and the icon (top-right corner).
Add-Type -AssemblyName System.Drawing
New-Item "$out\wizard" -ItemType Directory | Out-Null
function New-Bmp($w, $h, $png, $size, $file, $back) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml($back))
    $g.InterpolationMode = "HighQualityBicubic"
    $img = [System.Drawing.Image]::FromFile("$root\brand\png\$png")
    $g.DrawImage($img, [int](($w - $size) / 2), [int](($h - $size) / 2), $size, $size)
    $bmp.Save("$out\wizard\$file", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $img.Dispose(); $g.Dispose(); $bmp.Dispose()
}
New-Bmp 164 314 "icon-128.png" 128 "side.bmp" "#05070D"
New-Bmp 55 55 "icon-64.png" 44 "small.bmp" "#05070D"

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup" }
& $iscc "/DAppVersion=$Version" "$PSScriptRoot\ClientSSH.iss"
